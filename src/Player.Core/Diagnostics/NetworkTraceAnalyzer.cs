using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Player.Core.Diagnostics;

public sealed record NetworkTraceContext(int RootProcessId, int ControlProcessId, DateTimeOffset StartedUtc, DateTimeOffset EndedUtc,
    long EventsLost, long LogBuffersLost, long RealTimeBuffersLost);
public sealed record NetworkTraceResult(string Status, int RootProcessId, int ControlNetworkEvents, int ApplicationNetworkEvents,
    int UnattributedNetworkEvents, int RootStartEvents, int[] DescendantProcessIds, int ParsedEvents, bool LossFree, bool PositiveControlBeforeAndAfter,
    string Method = "Microsoft-Windows-Kernel-Network and Kernel-Process ETW; owned loopback positive control; PID and descendant attribution");

/// <summary>Evidence analysis never turns a missing/lossy trace or absent positive control into zero-traffic success.</summary>
public static class NetworkTraceAnalyzer
{
    public static readonly Guid NetworkProvider = new("7dd42a49-5329-4832-8dfd-43d979153a88");
    public static readonly Guid ProcessProvider = new("22fb2cd6-0e7b-422b-a0c7-2fad1fd0e716");
    private sealed record Event(Guid Provider, int Id, DateTimeOffset Time, Dictionary<string, string> Data);
    public static NetworkTraceResult Analyze(Stream trace, NetworkTraceContext context)
    {
        if (context.RootProcessId <= 0 || context.ControlProcessId <= 0 || context.RootProcessId == context.ControlProcessId ||
            context.StartedUtc >= context.EndedUtc || context.EventsLost < 0 || context.LogBuffersLost < 0 || context.RealTimeBuffersLost < 0)
            throw new ArgumentException("Invalid trace interval, process identities or loss counters.", nameof(context));
        using var reader = XmlReader.Create(trace, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 128L * 1024 * 1024, IgnoreWhitespace = true, CloseInput = false });
        var events = new List<Event>(); var parsed = 0;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Event") continue;
            using var subtree = reader.ReadSubtree(); var element = XElement.Load(subtree);
            if (++parsed > 500000) throw new InvalidDataException("Trace exceeds the bounded event limit.");
            var system = element.Elements().SingleOrDefault(e => e.Name.LocalName == "System");
            if (system is null) continue;
            var provider = system.Elements().FirstOrDefault(e => e.Name.LocalName == "Provider");
            if (!Guid.TryParse(provider?.Attribute("Guid")?.Value, out var guid) || guid != NetworkProvider && guid != ProcessProvider) continue;
            var stamp = system.Elements().FirstOrDefault(e => e.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value;
            if (!DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)) throw new InvalidDataException("Provider event has no valid timestamp.");
            if (!int.TryParse(system.Elements().FirstOrDefault(e => e.Name.LocalName == "EventID")?.Value, CultureInfo.InvariantCulture, out var id))
                throw new InvalidDataException("Provider event has no valid event ID.");
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in element.Descendants().Where(e => e.Name.LocalName == "Data" && e.Attribute("Name") is not null))
                if (!data.TryAdd(field.Attribute("Name")!.Value, field.Value)) throw new InvalidDataException("Duplicate trace field.");
            events.Add(new(guid, id, time, data));
        }
        var descendants = new HashSet<int>(); var starts = 0;
        var active = new HashSet<int>(); var owned = new HashSet<int>();
        var controls = 0; var application = 0; var unknown = 0;
        var controlTimes = new List<DateTimeOffset>(); DateTimeOffset? rootStart = null, rootEnd = null;
        foreach (var e in events.OrderBy(e => e.Time).ThenBy(e => e.Provider == ProcessProvider ? 0 : 1))
        {
            if (e.Time < context.StartedUtc || e.Time > context.EndedUtc) continue;
            var pid = Integer(e.Data, "ProcessID", "PID");
            if (e.Provider == ProcessProvider)
            {
                if (pid is null) continue;
                if (e.Id == 1 && (pid == context.RootProcessId || Integer(e.Data, "ParentProcessID", "ParentID") is { } parent && active.Contains(parent)))
                {
                    if (pid == context.RootProcessId) { starts++; rootStart = e.Time; }
                    else descendants.Add(pid.Value);
                    if (!active.Add(pid.Value)) throw new InvalidDataException("Overlapping process lifetimes in the trace.");
                    owned.Add(pid.Value);
                }
                else if (e.Id == 1) { owned.Remove(pid.Value); active.Remove(pid.Value); } // Foreign PID reuse.
                else if (e.Id == 2) { active.Remove(pid.Value); if (pid == context.RootProcessId) rootEnd = e.Time; }
            }
            else
            {
                if (pid is null) { unknown++; continue; }
                if (pid == context.ControlProcessId) { controls++; controlTimes.Add(e.Time); }
                // Kernel completion can be reported after process exit; retain ownership
                // until an observed foreign process actually reuses the PID.
                if (owned.Contains(pid.Value)) application++;
            }
        }
        var lossFree = context.EventsLost == 0 && context.LogBuffersLost == 0 && context.RealTimeBuffersLost == 0;
        var beforeAndAfter = rootStart is { } began && rootEnd is { } ended && controlTimes.Any(t => t < began) && controlTimes.Any(t => t > ended);
        var sufficient = lossFree && beforeAndAfter && starts == 1 && unknown == 0;
        return new(sufficient ? application == 0 ? "no-app-network-events-observed" : "application-network-events-observed" : "insufficient-evidence",
            context.RootProcessId, controls, application, unknown, starts, descendants.Order().ToArray(), parsed, lossFree, beforeAndAfter);
    }
    private static int? Integer(Dictionary<string, string> data, params string[] names)
    {
        foreach (var name in names)
            if (data.TryGetValue(name, out var value))
            {
                var style = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? NumberStyles.HexNumber : NumberStyles.Integer;
                if (int.TryParse(style == NumberStyles.HexNumber ? value[2..] : value, style, CultureInfo.InvariantCulture, out var pid) && pid > 0) return pid;
                return null;
            }
        return null;
    }
}
