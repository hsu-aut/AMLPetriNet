using System.Text;
using System.Xml;

namespace PtMapper.Tool.Analysis;

/// <summary>
/// The report as JUnit XML for a CI dashboard: one test suite per net, one test case per rule.
/// A rule fails its test case when it has an error or a warning; information never fails one.
/// </summary>
public static class FindingsJUnit
{
    public static string Write(AnalyzeReport report)
    {
        var rules = RuleCatalog.For(report);
        // Where the analysis was not called, no behavioural rule is known: one case says that it did not run.
        var behaviourKnown = rules.Any(rule => !RuleCatalog.IsStructural(rule.Id));
        var suites = report.Nets.Select(net =>
        {
            var cases = rules.Select(rule => Case(net, rule)).ToList();
            if (!behaviourKnown && !net.Analysed)
                cases.Add(new TestCase(new RuleCatalog.Rule("behaviour", "", "behaviour"), null, net.Problem ?? "the net was not analysed", ""));
            return (Net: net, Cases: cases);
        }).ToList();

        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineChars = "\n", OmitXmlDeclaration = false };
        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, settings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("testsuites");
            xml.WriteAttributeString("name", "ptmap");
            xml.WriteAttributeString("tests", suites.Sum(s => s.Cases.Count).ToString());
            xml.WriteAttributeString("failures", suites.Sum(s => s.Cases.Count(c => c.Failure != null)).ToString());
            xml.WriteAttributeString("errors", "0");
            xml.WriteAttributeString("skipped", suites.Sum(s => s.Cases.Count(c => c.Skipped != null)).ToString());

            foreach (var (net, cases) in suites)
            {
                var name = RuleCatalog.Location(net, null);
                xml.WriteStartElement("testsuite");
                xml.WriteAttributeString("name", name);
                xml.WriteAttributeString("tests", cases.Count.ToString());
                xml.WriteAttributeString("failures", cases.Count(c => c.Failure != null).ToString());
                xml.WriteAttributeString("errors", "0");
                xml.WriteAttributeString("skipped", cases.Count(c => c.Skipped != null).ToString());

                foreach (var testCase in cases)
                {
                    xml.WriteStartElement("testcase");
                    xml.WriteAttributeString("classname", name.Replace('/', '.'));
                    xml.WriteAttributeString("name", testCase.Rule.Title.Length == 0 ? testCase.Rule.Id : $"{testCase.Rule.Id} {testCase.Rule.Title}");

                    if (testCase.Skipped != null)
                    {
                        xml.WriteStartElement("skipped");
                        xml.WriteAttributeString("message", testCase.Skipped);
                        xml.WriteEndElement();
                    }
                    else if (testCase.Failure is { } failure)
                    {
                        xml.WriteStartElement("failure");
                        xml.WriteAttributeString("type", failure.Type);
                        xml.WriteAttributeString("message", failure.Message);
                        xml.WriteString(failure.Body);
                        xml.WriteEndElement();
                    }

                    if (testCase.Notes.Length > 0) xml.WriteElementString("system-out", testCase.Notes);
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        return new UTF8Encoding(false).GetString(stream.ToArray()) + "\n";
    }

    private sealed record Failure(string Type, string Message, string Body);

    private sealed record TestCase(RuleCatalog.Rule Rule, Failure? Failure, string? Skipped, string Notes);

    private static TestCase Case(NetReport net, RuleCatalog.Rule rule)
    {
        // A net that was not analysed has no answer to the rules about what it does.
        if (!net.Analysed && !RuleCatalog.IsStructural(rule.Id))
            return new TestCase(rule, null, net.Problem ?? "the net was not analysed", "");

        var own = net.Findings.Where(f => f.Rule == rule.Id).ToList();
        var failing = own.Where(f => f.Severity != "information").ToList();
        var notes = string.Join("\n", own.Where(f => f.Severity == "information").Select(Line));

        if (failing.Count == 0) return new TestCase(rule, null, null, notes);

        var type = failing.Any(f => f.Severity == "error") ? "error" : "warning";
        var first = failing[0].Message;
        var end = first.IndexOf(". ", StringComparison.Ordinal);
        return new TestCase(rule, new Failure(type, end > 0 ? first[..(end + 1)] : first, string.Join("\n", failing.Select(Line))), null, notes);
    }

    private static string Line(ReportedFinding finding) =>
        $"{finding.Severity.ToUpperInvariant(),-12} {finding.Rule,-5} {(finding.Element ?? "-"),-12} {finding.Message}";
}
