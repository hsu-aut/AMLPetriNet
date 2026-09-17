// Web front end for the PT mapper: the same conversion library the AML Editor
// plugin uses, behind a handful of endpoints, with the modeler served from the
// same origin so the page needs no proxy and no CORS.
//
// Page and API are one app, so it runs on any host that runs ASP.NET Core, and
// a reverse proxy can put it under a path of another site.
//
// The interesting endpoint is /api/update. Converting AML to PNML and back
// through /api/to-aml would produce a document holding the net and nothing else;
// /api/update writes an edited net into the document it came from, which is what
// keeps foreign attributes, links into other hierarchies and the document's own
// ids intact. That is the property the round-trip validation checks, so the web
// demo should exercise it rather than the lossy path.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Aml.Engine.CAEX;
using Microsoft.AspNetCore.HttpOverrides;
using PtMapper.Conversion;

var builder = WebApplication.CreateBuilder(args);

// A Petri net is small. The cap keeps a stray upload from filling the worker's
// memory, and AML documents with libraries inlined still fit comfortably.
const int MaxBodyBytes = 8 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxBodyBytes);

// Behind the App Service front end every request arrives from the front end's
// address; the client is in X-Forwarded-For. Without this the limiter below
// had one bucket for everybody, and fifteen page loads by anyone locked the
// site for the rest of the minute. KnownProxies is cleared because the front
// end's addresses are not fixed, and the header cannot be forged past it.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Only the conversion endpoints are limited. The page and its bundle are
// static files and were burning permits on every load.
const string ApiPolicy = "api";
// A reverse proxy on another host (aml.fpbjs.net serves this app under /pt)
// is one caller to the App Service front end, so every visitor behind it would
// share one bucket. The proxy names the visitor in X-Client-Address and proves
// that it is the proxy with a key both sides hold in PT_PROXY_KEY. Without the
// key the header is ignored, so nobody can choose their own bucket.
var proxyKey = builder.Configuration["PT_PROXY_KEY"];

string ClientAddress(HttpContext context)
{
    var remote = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (string.IsNullOrEmpty(proxyKey)) return remote;

    var sentKey = context.Request.Headers["X-Proxy-Key"].ToString();
    var client = context.Request.Headers["X-Client-Address"].ToString();
    var trusted = CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(sentKey), Encoding.UTF8.GetBytes(proxyKey));
    return trusted && client.Length > 0 ? "proxied:" + client : remote;
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ApiPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    options.OnRejected = async (context, token) =>
    {
        // A body and a Retry-After, so the page can say something better than
        // "429" and a client knows when to come back.
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests from this address. Please wait a minute." }, token);
    };
});

var app = builder.Build();

app.UseForwardedHeaders();

// Not in Development: the certificate on the Azure host is real, and a strict
// transport header on localhost would pin a developer's browser to https for
// a site that does not serve it.
if (!app.Environment.IsDevelopment()) app.UseHsts();

app.Use(async (context, next) =>
{
    // The page serves user-supplied XML back as a download; a browser guessing
    // a content type for it is the one thing that could turn a file into a
    // script. Nothing here relies on sniffing.
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

app.UseRateLimiter();
app.UseDefaultFiles();

// .aml and .pnml are not in the default MIME table, and an unknown extension is
// not served at all, so the demo file would come back as 404.
var contentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypes.Mappings[".aml"] = "application/xml";
contentTypes.Mappings[".pnml"] = "application/xml";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = context =>
    {
        // The bundle and the page change with every deploy and keep their
        // names, so a cached copy would leave a visitor on the old modeler
        // with no way to tell. Revalidating on every load costs one request.
        context.Context.Response.Headers.CacheControl = "no-cache";
    },
});

// ── conversions ─────────────────────────────────────────────────────────────

/// AML in, PNML out. The hierarchy can be named; without a name the first one
/// holding a net wins, and the response says which that was.
app.MapPost("/api/to-pnml", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var aml = await Body(ctx);
    var doc = CAEXDocument.LoadFromString(aml);

    var wanted = ctx.Request.Query["hierarchy"].ToString();
    var hierarchy = string.IsNullOrEmpty(wanted)
        ? doc.CAEXFile.InstanceHierarchy.FirstOrDefault(CaexToPtNet.ContainsNet)
        : doc.CAEXFile.InstanceHierarchy[wanted];

    if (hierarchy == null)
        throw new BadRequest(string.IsNullOrEmpty(wanted)
            ? "This document holds no Petri net."
            : $"This document has no InstanceHierarchy '{wanted}'.");

    var nets = CaexToPtNet.Read(hierarchy);
    if (nets.Count == 0) throw new BadRequest($"'{hierarchy.Name}' holds no Petri net.");

    var net = nets[0];

    // Ids that are not valid XML names would be refused by the modeler's
    // parser. The reader already adjusts them on the way out, so this call is a
    // safety net for anything it could not reach; what the page reports is
    // counted on the document instead, where the original values are.
    PtIdText.NormaliseIds(net);
    var adjusted = StoredIdsNeedingAdjustment(hierarchy);
    var arranged = PtLayout.ArrangeMissing(net);

    ctx.Response.Headers["X-Pt-Hierarchy"] = hierarchy.Name;
    ctx.Response.Headers["X-Pt-Info"] = JsonSerializer.Serialize(new
    {
        hierarchy = hierarchy.Name,
        hierarchies = doc.CAEXFile.InstanceHierarchy
            .Where(CaexToPtNet.ContainsNet).Select(h => h.Name).ToArray(),
        nets = nets.Count,
        places = net.Places.Count,
        transitions = net.Transitions.Count,
        arcs = net.Arcs.Count,
        adjustedIds = adjusted,
        arrangedNodes = arranged,

        // Elements of the document the diagram cannot show, such as an arc
        // whose link is missing. They stay in the document; the page says why
        // they are not on the canvas. A few are enough to explain, and a header
        // has to stay small.
        warningCount = net.Warnings.Count,
        warnings = net.Warnings.Take(5).ToArray(),
    });

    await Text(ctx, "application/xml", Pnml.Write(net));
})).RequireRateLimiting(ApiPolicy);

/// PNML in, a new AML document out. Everything PNML cannot express is absent
/// from the result by definition; /api/update is the lossless direction.
app.MapPost("/api/to-aml", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var nets = Pnml.ReadAll(await Body(ctx));
    if (nets.Count == 0) throw new BadRequest("This PNML file holds no net.");

    foreach (var net in nets) PtLayout.ArrangeMissing(net);

    var doc = CAEXDocument.New_CAEXDocument();
    doc.CAEXFile.FileName = FileName(ctx, "net.aml");

    var name = ctx.Request.Query["hierarchy"].ToString();
    PtNetToCaex.AppendInto(doc, nets, string.IsNullOrWhiteSpace(name) ? null : name);

    var warnings = nets.SelectMany(n => n.Warnings).ToList();
    ctx.Response.Headers["X-Pt-Info"] = JsonSerializer.Serialize(new
    {
        warningCount = warnings.Count,
        warnings = warnings.Take(5).ToArray(),
    });

    await Text(ctx, "application/xml", Xml(doc));
})).RequireRateLimiting(ApiPolicy);

/// The direction that keeps a document intact: the edited net as PNML, written
/// back into the AML it came from.
app.MapPost("/api/update", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var payload = await JsonSerializer.DeserializeAsync<UpdateRequest>(
        ctx.Request.Body, Json.Options) ?? throw new BadRequest("Empty request.");

    if (string.IsNullOrWhiteSpace(payload.Aml)) throw new BadRequest("No AML document was sent.");
    if (string.IsNullOrWhiteSpace(payload.Pnml)) throw new BadRequest("No net was sent.");

    var doc = CAEXDocument.LoadFromString(payload.Aml);
    var hierarchy = string.IsNullOrWhiteSpace(payload.Hierarchy)
        ? doc.CAEXFile.InstanceHierarchy.FirstOrDefault(CaexToPtNet.ContainsNet)
        : doc.CAEXFile.InstanceHierarchy[payload.Hierarchy];

    if (hierarchy == null) throw new BadRequest("The document no longer holds that hierarchy.");

    var summary = PtNetUpdater.UpdateInPlace(doc, hierarchy, Pnml.Read(payload.Pnml));

    ctx.Response.Headers["X-Pt-Info"] = JsonSerializer.Serialize(new
    {
        hierarchy = hierarchy.Name,
        added = summary.Added,
        updated = summary.Updated,
        removed = summary.Removed,
        notes = summary.Notes,
    });

    await Text(ctx, "application/xml", Xml(doc));
})).RequireRateLimiting(ApiPolicy);

/// The ISO/IEC 15909-1 rules, on whichever of the two formats is sent.
app.MapPost("/api/validate", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var body = await Body(ctx);
    var net = LooksLikeAml(body)
        ? CaexToPtNet.ReadAll(CAEXDocument.LoadFromString(body)).FirstOrDefault()
          ?? throw new BadRequest("This document holds no Petri net.")
        : Pnml.Read(body);

    var findings = PtValidator.Validate(net);

    await ctx.Response.WriteAsJsonAsync(new
    {
        net = net.Name ?? net.Id,
        findings = findings.Select(f => new
        {
            rule = f.Rule,
            severity = f.Severity.ToString().ToLowerInvariant(),
            message = f.Message,
            element = f.ElementId,
        }),
    });
})).RequireRateLimiting(ApiPolicy);

/// The domain library as a file, so a visitor can look at what this maps onto.
app.MapGet("/api/library", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var doc = CAEXDocument.New_CAEXDocument();
    doc.CAEXFile.FileName = PtNames.LibraryFileName;
    PtLibraries.EnsureLibraries(doc.CAEXFile);

    ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{PtNames.LibraryFileName}\"";
    await Text(ctx, "application/xml", Xml(doc));
})).RequireRateLimiting(ApiPolicy);

app.MapGet("/api/health", (HttpContext ctx) => Results.Ok(new
{
    status = "ok",
    library = PtNames.LibraryVersion,
    utc = DateTime.UtcNow,
    // The address the rate limiter partitions on. Behind the App Service
    // front end this has to be the caller's public address, not the front
    // end's; showing it is how that gets checked after a deploy.
    client = ctx.Connection.RemoteIpAddress?.ToString(),
}));

app.Run();

// ── plumbing ────────────────────────────────────────────────────────────────

/// <summary>
/// How many elements carry an id the modeler's parser would refuse, which is
/// what the MPS500 example does with its braced CAEX GUIDs. The document keeps
/// them; the count exists so the page can say so rather than pretend.
/// </summary>
static int StoredIdsNeedingAdjustment(InstanceHierarchyType hierarchy) =>
    hierarchy.InternalElement
        .SelectMany(net => net.InternalElement.Prepend(net))
        .Select(e => e.Attribute[PtNames.Attributes.Identification]?
            .Attribute[PtNames.Attributes.Id]?.Value)
        .Count(id => !string.IsNullOrEmpty(id) && !PtIdText.IsXmlName(id!));

static async Task<string> Body(HttpContext ctx)
{
    using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    var text = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(text)) throw new BadRequest("The request body is empty.");
    return text;
}

static async Task Text(HttpContext ctx, string contentType, string body)
{
    ctx.Response.ContentType = contentType;
    await ctx.Response.WriteAsync(body);
}

static string Xml(CAEXDocument doc)
{
    using var stream = doc.SaveToStream(prettyPrint: true);
    stream.Position = 0;
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return reader.ReadToEnd();
}

/// <summary>A sanitised file name from the query, never a path.</summary>
static string FileName(HttpContext ctx, string fallback)
{
    var requested = ctx.Request.Query["name"].ToString();
    if (string.IsNullOrWhiteSpace(requested)) return fallback;

    var name = Path.GetFileName(requested);
    return string.IsNullOrWhiteSpace(name) || name.Length > 120 ? fallback : name;
}

static bool LooksLikeAml(string text) =>
    text.Contains("CAEXFile", StringComparison.Ordinal);

/// <summary>
/// Turns a failure into a message the page can show. A stack trace would say
/// nothing to a visitor and would expose more than it should.
/// </summary>
static async Task Guarded(HttpContext ctx, Func<Task> action)
{
    try
    {
        await action();
    }
    catch (BadRequest bad)
    {
        await Fail(ctx, StatusCodes.Status400BadRequest, bad.Message);
    }
    catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException or ArgumentException
                                  or FormatException or JsonException or BadHttpRequestException)
    {
        // The caller's file, not the server: a malformed document, a PNML file
        // without a net, a JSON body that is not the expected shape, a body
        // over the size limit.
        await Fail(ctx, StatusCodes.Status400BadRequest, "This file could not be read: " + ex.Message);
    }
    catch (Exception ex)
    {
        // Logged as well as answered: a 500 with nothing in the log leaves
        // nobody able to say what happened on the server afterwards.
        ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PtMapper.Web")
            .LogError(ex, "Unhandled failure in {Path}", ctx.Request.Path);

        await Fail(ctx, StatusCodes.Status500InternalServerError, "Conversion failed: " + ex.Message);
    }
}

static async Task Fail(HttpContext ctx, int status, string message)
{
    if (ctx.Response.HasStarted) return;
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { error = message });
}

file static class Json
{
    /// <summary>The page sends camelCase; the record is PascalCase.</summary>
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}

/// <summary>A failure that is the caller's fault and safe to describe.</summary>
file sealed class BadRequest(string message) : Exception(message);

file sealed record UpdateRequest(string? Aml, string? Pnml, string? Hierarchy);
