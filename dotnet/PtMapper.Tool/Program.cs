// Command line front end for the PT mapper. Useful without the AML Editor:
// for regenerating the files in examples/paper, for checking a foreign PNML
// file, and for the end-to-end round-trip check the web test drives.
//
//   ptmap to-aml   <in.pnml> <out.aml> [hierarchy-name]
//   ptmap to-pnml  <in.aml>  <out.pnml> [hierarchy-name]
//   ptmap roundtrip <in.aml> [hierarchy-name]   aml -> pnml -> aml, and back
//   ptmap compare <before.aml> <after.aml> [hierarchy-name]   what a session changed
//   ptmap library  <out.aml>            emit the ISO_PT domain library
//   ptmap sample   <out.pnml>           emit a small demo net
//   ptmap fixtures <out-dir>            emit the interop fixture set
//   ptmap validate <in.pnml|in.aml>     check against the ISO/IEC 15909-1 rules
//   ptmap analyze  <in.pnml|in.aml> [hierarchy] [options]   structural and behavioural findings
//   ptmap report   <in.pnml|in.aml> <out.md>   write a conformance report
//   ptmap selfcontained <in.aml> <out.aml> [libDir]   inline the libraries
//   ptmap editor-test <out-dir> [libDir]      write the AML Editor test files

using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using PtMapper.Tool.Analysis;

if (args.Length == 0)
{
    Usage();
    return 1;
}

try
{
    switch (args[0])
    {
        case "to-aml" when args.Length >= 3:
        {
            if (!File.Exists(args[1]))
            {
                Console.Error.WriteLine($"{args[1]}: no such file.");
                return 2;
            }

            // From the bytes: the file may declare an encoding of its own.
            using var input = File.OpenRead(args[1]);
            var nets = Pnml.ReadAll(input);
            var document = CAEXDocument.New_CAEXDocument();
            document.CAEXFile.FileName = Path.GetFileName(args[2]);
            PtNetToCaex.AppendInto(document, nets, args.Length > 3 ? args[3] : null);
            document.SaveToFile(args[2], prettyPrint: true);
            PrintWarnings(nets);
            Console.WriteLine($"{args[2]}: {nets.Count} net(s), "
                + $"{nets.Sum(n => n.Places.Count)} places, {nets.Sum(n => n.Transitions.Count)} transitions, "
                + $"{nets.Sum(n => n.Arcs.Count)} arcs");
            return 0;
        }

        case "to-pnml" when args.Length >= 3:
        {
            if (!File.Exists(args[1]))
            {
                Console.Error.WriteLine($"{args[1]}: no such file.");
                return 2;
            }

            var document = CAEXDocument.LoadFromFile(args[1]);

            // A document may hold more than one hierarchy with a net, as the
            // MPS500 bridging example does; naming one picks it.
            var hierarchy = args.Length >= 4 ? document.CAEXFile.InstanceHierarchy[args[3]] : null;
            if (args.Length >= 4 && hierarchy == null)
            {
                Console.Error.WriteLine($"{args[1]} has no InstanceHierarchy '{args[3]}'.");
                return 2;
            }

            var nets = hierarchy == null ? CaexToPtNet.ReadAll(document) : CaexToPtNet.Read(hierarchy);
            if (nets.Count == 0)
            {
                Console.Error.WriteLine(hierarchy == null
                    ? $"{args[1]} contains no PT_Net instance hierarchy."
                    : $"{args[1]}: the InstanceHierarchy '{hierarchy.Name}' holds no Petri net.");
                return 2;
            }
            if (nets.Count > 1)
                Console.Error.WriteLine($"note: {nets.Count} nets found, writing the first one.");

            // Indented: this file is meant to be opened and read.
            File.WriteAllText(args[2], Pnml.Write(nets[0], indent: true));
            PrintWarnings(nets.Take(1));
            Console.WriteLine($"{args[2]}: {nets[0].Places.Count} places, {nets[0].Transitions.Count} transitions, {nets[0].Arcs.Count} arcs");
            return 0;
        }

        case "library" when args.Length >= 2:
        {
            var document = CAEXDocument.New_CAEXDocument();
            document.CAEXFile.FileName = Path.GetFileName(args[1]);
            PtLibraries.EnsureLibraries(document.CAEXFile);
            document.SaveToFile(args[1], prettyPrint: true);
            Console.WriteLine($"{args[1]}: ISO_PT domain library v{PtNames.LibraryVersion}");
            return 0;
        }

        case "roundtrip" when args.Length >= 2:
        {
            // The round-trip check of PaperRoundTripTests, runnable against
            // whatever the AML Editor saved rather than only against a fixture.
            var report = PtRoundTripCheck.Run(
                CAEXDocument.LoadFromFile(args[1]),
                args.Length >= 3 ? args[2] : null);

            Console.WriteLine(report);
            Console.WriteLine(report.Passed
                ? "round trip: everything checked came back unchanged"
                : "round trip: see the failing lines above");
            return report.Passed ? 0 : 4;
        }

        case "compare" when args.Length >= 3:
        {
            // What an AML Editor session changed between opening and saving.
            var report = PtRoundTripCheck.Compare(
                CAEXDocument.LoadFromFile(args[1]),
                CAEXDocument.LoadFromFile(args[2]),
                args.Length >= 4 ? args[3] : null);

            Console.WriteLine(report);
            return report.Passed ? 0 : 4;
        }

        case "validate" when args.Length >= 2:
        {
            var nets = ReadNets(args[1]);
            if (nets.Count == 0)
            {
                Console.Error.WriteLine($"{args[1]} contains no net.");
                return 2;
            }

            var errors = 0;
            PrintWarnings(nets);
            foreach (var net in nets)
            {
                var findings = PtValidator.Validate(net);
                Console.WriteLine($"{net.Name ?? net.Id}: {findings.Count} finding(s)");
                foreach (var finding in findings) Console.WriteLine("  " + finding);
                errors += findings.Count(f => f.Severity == PtSeverity.Error);
            }

            // A non-zero exit on errors makes this usable as a CI gate.
            return errors > 0 ? 4 : 0;
        }

        case "analyze" when args.Length >= 2:
        {
            // UTF-8 without a byte order mark, whatever the code page of the
            // console is: the output is read by a parser or a person on another
            // machine, and names in it can hold umlauts.
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            return AnalyzeCommand.Run(args[1..], stdout, Console.Error, new NodeAnalysisRunner());
        }

        case "fixtures" when args.Length >= 2:
        {
            Directory.CreateDirectory(args[1]);
            foreach (var (name, net) in Fixtures())
                File.WriteAllText(Path.Combine(args[1], name + ".pnml"), Pnml.Write(net));
            Console.WriteLine($"{args[1]}: {Fixtures().Count} fixture(s)");
            return 0;
        }

        case "report" when args.Length >= 3:
        {
            var nets = ReadNets(args[1]);
            if (nets.Count == 0)
            {
                Console.Error.WriteLine($"{args[1]} contains no net.");
                return 2;
            }

            var report = string.Join(Environment.NewLine + Environment.NewLine,
                nets.Select(n => PtConformanceReport.Build(n, PtValidator.Validate(n), Path.GetFileName(args[1]))));
            File.WriteAllText(args[2], report);
            Console.WriteLine($"{args[2]}: report for {nets.Count} net(s)");
            return 0;
        }

        case "selfcontained" when args.Length >= 3:
        {
            var libraries = PtSelfContained.ReadLibraries(args.Length > 3 ? args[3] : "libraries");
            var (xml, inlined) = PtSelfContained.Inline(File.ReadAllText(args[1]), libraries);
            File.WriteAllText(args[2], xml);
            Console.WriteLine($"{args[2]}: inlined {string.Join(", ", inlined)}");
            return 0;
        }

        case "editor-test" when args.Length >= 2:
        {
            var libDir = args.Length > 2 ? args[2] : "libraries";
            Directory.CreateDirectory(args[1]);
            foreach (var line in WriteEditorTestFiles(args[1], libDir)) Console.WriteLine(line);
            return 0;
        }

        case "sample" when args.Length >= 2:
        {
            File.WriteAllText(args[1], Pnml.Write(SampleNet()));
            Console.WriteLine(args[1] + ": sample net");
            return 0;
        }

        default:
            Usage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 3;
}

static void Usage()
{
    Console.Error.WriteLine("""
        ptmap to-aml   <in.pnml> <out.aml> [hierarchy-name]
        ptmap to-pnml  <in.aml>  <out.pnml> [hierarchy-name]
        ptmap roundtrip <in.aml> [hierarchy-name]
        ptmap compare <before.aml> <after.aml> [hierarchy-name]
        ptmap library  <out.aml>
        ptmap sample   <out.pnml>
        ptmap fixtures <out-dir>
        ptmap validate <in.pnml|in.aml>
        ptmap analyze  <in.pnml|in.aml> [hierarchy] [--format text|json|sarif|junit] [--output file]
                       [--fail-on error|warning|never] [--markings N] [--time-limit ms] [--timeout s]
                       [--all] [--require-complete]
        ptmap report   <in.pnml|in.aml> <out.md>
        ptmap selfcontained <in.aml> <out.aml> [libDir]
        ptmap editor-test <out-dir> [libDir]
        """);
}

// What reading left out or had to change. On stderr, so a script that pipes
// the output still gets the result alone.
static void PrintWarnings(IEnumerable<PtNet> nets)
{
    foreach (var net in nets)
    foreach (var warning in net.Warnings)
        Console.Error.WriteLine("note: " + warning);
}

/// <summary>
/// Reads nets from either format, so every command takes both. A PNML file may
/// hold several nets and all of them are returned; taking only the first once
/// meant `validate` passed a file whose second net was broken.
/// </summary>
static List<PtNet> ReadNets(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException($"{path}: no such file.", path);

    if (path.EndsWith(".aml", StringComparison.OrdinalIgnoreCase))
        return CaexToPtNet.ReadAll(CAEXDocument.LoadFromFile(path));

    using var stream = File.OpenRead(path);
    return Pnml.ReadAll(stream);
}

/// <summary>
/// Writes the files meant to be opened in the AML Editor by hand.
///
/// Both are self-contained: the AML Editor does not resolve file-based
/// ExternalReferences, so a document that only points at its libraries opens
/// with every class path unresolved and is useless for a test.
/// </summary>
static List<string> WriteEditorTestFiles(string outDir, string libDir)
{
    var libraries = PtSelfContained.ReadLibraries(libDir);
    if (libraries.Count == 0)
        throw new FileNotFoundException($"No library files found in '{libDir}'. Pass the directory as the last argument.");

    var lines = new List<string>();

    // 1 ── the sample net on its own, for the ordinary walkthrough.
    var netDoc = PtNetToCaex.Convert(SampleNet(), "QualityGate");
    netDoc.CAEXFile.FileName = "QualityGate.aml";
    lines.Add(Write(netDoc, Path.Combine(outDir, "QualityGate.aml")));

    // 2 ── the same net plus a plant hierarchy that links into it, so the
    //      cross-hierarchy regression case can be tried without building the
    //      link by hand in the editor first.
    var bridging = CAEXDocument.New_CAEXDocument();
    bridging.CAEXFile.FileName = "Bridging.aml";
    var ih = PtNetToCaex.AppendInto(bridging, SampleNet(), "QualityGate");

    var plant = bridging.CAEXFile.InstanceHierarchy.Append("MPS500_Plant");
    var station = plant.InternalElement.Append("QualityCheckStation");
    station.ID = "{b1a5e000-0001-4000-8000-000000000001}";

    var sensorOk = station.ExternalInterface.Append("Sensor_OK");
    sensorOk.ID = "{b1a5e000-0002-4000-8000-000000000001}";
    var sensorNok = station.ExternalInterface.Append("Sensor_NOK");
    sensorNok.ID = "{b1a5e000-0002-4000-8000-000000000002}";

    // Hook the two result places of the net to the station's sensors, the way
    // the MPS500 bridging example does.
    var netIE = ih.InternalElement.Single();
    foreach (var (placeId, sensor, linkName) in new[]
             {
                 ("p_ok", sensorOk, "Result_OK_from_sensor"),
                 ("p_nok", sensorNok, "Result_NOK_from_sensor"),
             })
    {
        var place = netIE.InternalElement.FirstOrDefault(e =>
            e.Attribute["Identification"]?.Attribute["id"]?.Value == placeId);
        if (place == null) continue;

        var hook = place.ExternalInterface.Append("PlantSignal");
        hook.ID = PtIds.For("editor-test-interface", placeId);
        // SignalInterface, not "Signal": the AML base library has no class of
        // that name, and an unresolvable path makes the editor show the
        // interface as broken in exactly the file meant to demonstrate that
        // the links survive. The alias comes from AmlBase so the two cannot
        // drift apart.
        hook.RefBaseClassPath =
            AmlBase.Alias + "@AutomationMLInterfaceClassLib/AutomationMLBaseInterface/SignalInterface";

        var link = station.InternalLink.Append(linkName);
        link.ID = PtIds.For("editor-test-link", placeId);
        link.RefPartnerSideA = sensor.ID;
        link.RefPartnerSideB = hook.ID;
    }

    lines.Add(Write(bridging, Path.Combine(outDir, "Bridging.aml")));
    return lines;

    string Write(CAEXDocument doc, string path)
    {
        var temp = Path.GetTempFileName();
        doc.SaveToFile(temp, prettyPrint: true);

        // The mapper embeds the shared libraries itself now, so this step
        // usually has nothing left to do. It stays because the inlining is what
        // this command promises, and a document from an older version of the
        // mapper still needs it.
        var (xml, inlined) = PtSelfContained.Inline(File.ReadAllText(temp), libraries);
        File.Delete(temp);
        File.WriteAllText(path, xml);

        return inlined.Count == 0
            ? $"{path}: self-contained (nothing left to inline)"
            : $"{path}: self-contained ({string.Join(", ", inlined)} inlined)";
    }
}

/// <summary>
/// The cases both PNML implementations have to agree on. Written by the C# side
/// and replayed through the modeler by web/tools/verify-interop.mjs, so a
/// convention that drifts on one side fails there rather than in a user's file.
/// </summary>
static List<(string Name, PtNet Net)> Fixtures()
{
    var list = new List<(string, PtNet)>();

    var empty = new PtNet { Id = "empty", Name = "Empty" };
    list.Add(("empty", empty));

    // A file that arrived without any layout, after the arrangement the plugin
    // applies before handing it to the modeler. Without that step every node
    // would sit at the origin, which is what this fixture guards against.
    var arranged = new PtNet { Id = "arranged", Name = "Arranged on import" };
    arranged.Places.Add(new PtPlace { Id = "p" });
    arranged.Transitions.Add(new PtTransition { Id = "t" });
    arranged.Places.Add(new PtPlace { Id = "p2" });
    arranged.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });
    arranged.Arcs.Add(new PtArc { Id = "b", SourceId = "t", TargetId = "p2" });
    PtLayout.ArrangeMissing(arranged);
    list.Add(("auto-arranged", arranged));

    // A named arc, and one whose name has to survive next to an inscription.
    // The modeler's importer once dropped every arc name, which no fixture
    // could see because none of them had one.
    var named = new PtNet { Id = "named-arcs", Name = "Named arcs" };
    named.Places.Add(new PtPlace { Id = "Idle", Name = "Idle", InitialMarking = 1, Bounds = new PtBounds(0, 0, 50, 50) });
    named.Transitions.Add(new PtTransition { Id = "Start", Name = "Start", Bounds = new PtBounds(150, 0, 50, 50) });
    named.Places.Add(new PtPlace { Id = "Checking", Name = "Checking", Bounds = new PtBounds(300, 0, 50, 50) });
    named.Arcs.Add(new PtArc { Id = "a1", Name = "Arc_Idle_to_Start", SourceId = "Idle", TargetId = "Start" });
    named.Arcs.Add(new PtArc { Id = "a2", Name = "Arc_Start_to_Checking", SourceId = "Start", TargetId = "Checking", Weight = 2 });
    list.Add(("named-arcs", named));

    var silent = new PtNet { Id = "silent", Name = "Silent transition" };
    silent.Places.Add(new PtPlace { Id = "p", Name = "P", InitialMarking = 1, Bounds = new PtBounds(0, 0, 50, 50) });
    silent.Transitions.Add(new PtTransition { Id = "t", Name = "tau", IsSilent = true, Bounds = new PtBounds(150, 0, 20, 50) });
    silent.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });
    list.Add(("silent", silent));

    // Markup in the text, not in the ids: PNML ids are xs:ID and must be valid
    // XML names, which rule PT11 checks. Names are free text and have to survive
    // escaping on both sides.
    var markup = new PtNet { Id = "markup", Name = "Markup & <chars>" };
    markup.Places.Add(new PtPlace { Id = "p1", Name = "a < b & c", Bounds = new PtBounds(0, 0, 50, 50) });
    markup.Transitions.Add(new PtTransition { Id = "t2", Name = "quote \" here", Bounds = new PtBounds(150, 0, 50, 50) });
    markup.Arcs.Add(new PtArc { Id = "a3", SourceId = "p1", TargetId = "t2", Weight = 5 });
    list.Add(("markup", markup));

    var offsets = new PtNet { Id = "offsets", Name = "Moved labels" };
    offsets.Places.Add(new PtPlace
    {
        Id = "p", Name = "Moved", Bounds = new PtBounds(0, 0, 50, 50),
        LabelOffset = new PtPoint(-30.5, 44),
    });
    offsets.Transitions.Add(new PtTransition { Id = "t", Name = "T", Bounds = new PtBounds(200, 100, 50, 50) });
    // A dragged weight label as well as a dragged node label: the arc's label
    // position goes through a different path in both converters, and without a
    // fixture that carries one the interop check could not see it.
    var bent = new PtArc
    {
        Id = "a", SourceId = "p", TargetId = "t", Weight = 12,
        LabelOffset = new PtPoint(18, -26),
    };
    bent.Waypoints.Add(new PtPoint(120, 25));
    bent.Waypoints.Add(new PtPoint(120, 125));
    offsets.Arcs.Add(bent);
    list.Add(("offsets-and-bends", offsets));

    return list;
}

/// <summary>
/// A quality gate that produces or rejects a part: two competing transitions on
/// the same place, resolved by the inspection result. Small enough to read at a
/// glance, and it exercises marking, weight and a bent arc.
/// </summary>
static PtNet SampleNet()
{
    var net = new PtNet { Id = "quality_gate", Name = "Quality gate" };

    net.Places.Add(new PtPlace { Id = "p_waiting", Name = "Waiting", InitialMarking = 2, Bounds = new PtBounds(100, 200, 50, 50) });
    net.Places.Add(new PtPlace { Id = "p_ok", Name = "Result OK", Bounds = new PtBounds(280, 100, 50, 50) });
    net.Places.Add(new PtPlace { Id = "p_nok", Name = "Result NOK", Bounds = new PtBounds(280, 300, 50, 50) });
    net.Places.Add(new PtPlace { Id = "p_shipped", Name = "Shipped", Bounds = new PtBounds(620, 100, 50, 50) });
    net.Places.Add(new PtPlace { Id = "p_scrapped", Name = "Scrapped", Bounds = new PtBounds(620, 300, 50, 50) });

    net.Transitions.Add(new PtTransition { Id = "t_inspect", Name = "Inspect", Bounds = new PtBounds(190, 200, 50, 50) });
    net.Transitions.Add(new PtTransition { Id = "t_approve", Name = "Approve", Bounds = new PtBounds(450, 100, 50, 50) });
    net.Transitions.Add(new PtTransition { Id = "t_reject", Name = "Reject", Bounds = new PtBounds(450, 300, 50, 50) });

    net.Arcs.Add(new PtArc { Id = "a1", SourceId = "p_waiting", TargetId = "t_inspect" });
    net.Arcs.Add(new PtArc { Id = "a2", SourceId = "t_inspect", TargetId = "p_ok" });
    net.Arcs.Add(new PtArc { Id = "a3", SourceId = "t_inspect", TargetId = "p_nok" });
    net.Arcs.Add(new PtArc { Id = "a4", SourceId = "p_ok", TargetId = "t_approve" });
    net.Arcs.Add(new PtArc { Id = "a5", SourceId = "t_approve", TargetId = "p_shipped" });
    net.Arcs.Add(new PtArc { Id = "a6", SourceId = "p_nok", TargetId = "t_reject" });
    net.Arcs.Add(new PtArc { Id = "a7", SourceId = "t_reject", TargetId = "p_scrapped" });

    // Rework consumes two scrapped parts to make one waiting part again, and
    // routes around the rest of the net.
    var rework = new PtArc { Id = "a8", SourceId = "p_scrapped", TargetId = "t_rework", Weight = 2 };
    net.Transitions.Add(new PtTransition { Id = "t_rework", Name = "Rework", Bounds = new PtBounds(360, 430, 50, 50) });
    net.Arcs.Add(rework);

    var back = new PtArc { Id = "a9", SourceId = "t_rework", TargetId = "p_waiting" };
    back.Waypoints.Add(new PtPoint(125, 455));
    net.Arcs.Add(back);

    return net;
}
