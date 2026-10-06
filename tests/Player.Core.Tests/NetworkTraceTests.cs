using System.Text;
using Player.Core.Diagnostics;

namespace Player.Core.Tests;

public sealed class NetworkTraceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-06T00:00:00Z");
    private static NetworkTraceContext Context => new(100, 200, Start, Start.AddMinutes(1), 0, 0, 0);
    private static string Event(Guid provider, int id, int second, params (string Name, string Value)[] fields) =>
        $"<Event><System><Provider Guid='{provider}'/><EventID>{id}</EventID><TimeCreated SystemTime='{Start.AddSeconds(second):O}'/></System><EventData>" +
        string.Join("", fields.Select(f => $"<Data Name='{f.Name}'>{f.Value}</Data>")) + "</EventData></Event>";
    private static NetworkTraceResult Analyze(NetworkTraceContext context, params string[] events)
    { using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<Events xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>" + string.Join("", events) + Event(NetworkTraceAnalyzer.ProcessProvider, 2, 50, ("ProcessID", "100")) + Event(NetworkTraceAnalyzer.NetworkProvider, 10, 51, ("PID", "200")) + "</Events>")); return NetworkTraceAnalyzer.Analyze(stream, context); }
    private static string Root => Event(NetworkTraceAnalyzer.ProcessProvider, 1, 3, ("ProcessID", "100"), ("ParentProcessID", "200"));
    private static string Control => Event(NetworkTraceAnalyzer.NetworkProvider, 10, 2, ("PID", "200"));
    [Fact] public void ZeroApplicationTrafficRequiresObservedPositiveControlAndRootStart()
    { var report = Analyze(Context, Root, Control, Event(NetworkTraceAnalyzer.NetworkProvider, 10, 3, ("PID", "999"))); Assert.Equal("no-app-network-events-observed", report.Status); Assert.Equal(0, report.ApplicationNetworkEvents); Assert.Equal(2, report.ControlNetworkEvents); Assert.True(report.PositiveControlBeforeAndAfter); }
    [Fact] public void MissingControlCannotPassAsZeroTraffic()
    { Assert.Equal("insufficient-evidence", Analyze(Context, Root).Status); Assert.Equal("insufficient-evidence", Analyze(Context, Control).Status); }
    [Theory] [InlineData(1, 0, 0)] [InlineData(0, 1, 0)] [InlineData(0, 0, 1)]
    public void LostEventsOrBuffersInvalidateTheEvidence(long events, long log, long realTime)
    { Assert.Equal("insufficient-evidence", Analyze(Context with { EventsLost = events, LogBuffersLost = log, RealTimeBuffersLost = realTime }, Root, Control).Status); }
    [Fact] public void ChildAndGrandchildNetworkEventsAreAttributedButPidReuseIsNot()
    {
        var report = Analyze(Context, Root, Control,
            Event(NetworkTraceAnalyzer.ProcessProvider, 1, 3, ("ProcessID", "0x65"), ("ParentProcessID", "100")),
            Event(NetworkTraceAnalyzer.ProcessProvider, 1, 4, ("ProcessID", "102"), ("ParentProcessID", "101")),
            Event(NetworkTraceAnalyzer.NetworkProvider, 10, 5, ("PID", "100")),
            Event(NetworkTraceAnalyzer.NetworkProvider, 10, 6, ("PID", "101")),
            Event(NetworkTraceAnalyzer.NetworkProvider, 10, 7, ("PID", "102")),
            Event(NetworkTraceAnalyzer.ProcessProvider, 2, 8, ("ProcessID", "101")),
            Event(NetworkTraceAnalyzer.ProcessProvider, 1, 9, ("ProcessID", "101"), ("ParentProcessID", "999")),
            Event(NetworkTraceAnalyzer.NetworkProvider, 10, 10, ("PID", "101")));
        Assert.Equal("application-network-events-observed", report.Status); Assert.Equal(3, report.ApplicationNetworkEvents); Assert.Equal(new[] { 101, 102 }, report.DescendantProcessIds);
    }
    [Fact] public void MissingPidIsNotSilentlyAttributedToSystemOrIgnored()
    { var report = Analyze(Context, Root, Control, Event(NetworkTraceAnalyzer.NetworkProvider, 11, 3)); Assert.Equal("insufficient-evidence", report.Status); Assert.Equal(1, report.UnattributedNetworkEvents); }
    [Fact] public void DtdAndExternalEntitiesAreRejected()
    { using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE Events [<!ENTITY e SYSTEM 'file:///private'>]><Events>&e;</Events>")); Assert.Throws<System.Xml.XmlException>(() => NetworkTraceAnalyzer.Analyze(stream, Context)); }
}
