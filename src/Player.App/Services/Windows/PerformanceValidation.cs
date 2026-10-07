using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Player.App.Views;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Owned 100k SQLite metadata dataset; actual production query/page WPF rendering and real playlist scroll.</summary>
public static class PerformanceValidation
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryKiB);
    internal static int Containers(DependencyObject parent)
    { var count = parent is ListBoxItem ? 1 : 0; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) count += Containers(VisualTreeHelper.GetChild(parent, i)); return count; }
    private static object Timings(List<double> samples, double target) => new
    {
        SamplesMilliseconds = samples, Count = samples.Count, P95Milliseconds = samples.Order().ElementAt((int)Math.Ceiling(samples.Count * .95) - 1),
        MaximumMilliseconds = samples.Max(), TargetMilliseconds = target, AllSamplesWithinTarget = samples.All(v => v <= target)
    };
    public static async Task<object> ScrollAsync(MainWindow window, ListBox list)
    {
        var samples = new List<double>(); var maximumContainers = 0;
        for (var i = 0; i < 32; i++)
        {
            var item = list.Items[(i * 997) % list.Items.Count]; var timer = Stopwatch.StartNew();
            list.ScrollIntoView(item); list.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            timer.Stop(); samples.Add(timer.Elapsed.TotalMilliseconds);
            if (list.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem) throw new InvalidOperationException("Scrolled row was not realized.");
            maximumContainers = Math.Max(maximumContainers, Containers(list));
        }
        if (maximumContainers is <= 0 or >= 150) throw new InvalidOperationException("Scroll realized unbounded row containers.");
        return new { ActualPlaylistRows = list.Items.Count, GlobalCapacity = 10000, Method = "ScrollIntoView + UpdateLayout + Dispatcher ContextIdle; software-driven WPF", MaximumRealizedContainers = maximumContainers, Timings = Timings(samples, 100) };
    }
    public static async Task<object> SearchAsync(MainWindow owner, string output)
    {
        var directory = Path.Combine(output, "stage-g-performance-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var model = ((App)Application.Current).CreateModel(directory); LibraryWindow? window = null;
        try
        {
            await model.InitializeAsync();
            var seed = Stopwatch.StartNew();
            await Task.Run(() =>
            {
                // Bulk seed only: measured operations use the production serialized index API and LibraryWindow page path.
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "library.db"), Pooling = false }.ToString()); connection.Open();
                using var transaction = connection.BeginTransaction(); var root = Guid.NewGuid();
                using (var command = connection.CreateCommand())
                { command.Transaction = transaction; command.CommandText = "INSERT INTO LibraryRoots(Id,Path,Enabled) VALUES($id,$path,1)"; command.Parameters.AddWithValue("$id", root.ToString()); command.Parameters.AddWithValue("$path", directory); command.ExecuteNonQuery(); }
                using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO MediaIndex(Id,RootId,Path,Search,Generation,Available,Json) VALUES($id,$root,$path,$search,'owned-performance',0,$json)";
                foreach (var name in new[] { "$id", "$root", "$path", "$search", "$json" }) insert.Parameters.AddWithValue(name, "");
                insert.Parameters["$root"].Value = root.ToString(); insert.Prepare();
                for (var i = 0; i < 100000; i++)
                {
                    var path = Path.Combine(directory, "metadata-only", i.ToString("D6") + ".wav"); var id = Guid.NewGuid();
                    var track = new MediaTrack(id, path, "Performance Музыка " + i.ToString("D6"), Available: false);
                    var file = new IndexedFile(id, root, path, 123, 456, false, "owned-performance", track);
                    insert.Parameters["$id"].Value = id.ToString(); insert.Parameters["$path"].Value = path.ToUpperInvariant();
                    insert.Parameters["$search"].Value = LibrarySearch.Fields(track); insert.Parameters["$json"].Value = JsonSerializer.Serialize(file); insert.ExecuteNonQuery();
                }
                transaction.Commit();
            }); seed.Stop();
            window = new LibraryWindow(owner, model); window.Show();
            await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            // Drain the Loaded query before collecting samples; it warms SQLite/WPF and is explicitly excluded.
            await model.LibraryIndex!.SearchAsync("warmup");
            await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            using var process = Process.GetCurrentProcess(); process.Refresh(); var cpuBefore = process.TotalProcessorTime;
            var workingBefore = process.WorkingSet64; var privateBefore = process.PrivateMemorySize64; var handlesBefore = process.HandleCount;
            var timer = Stopwatch.StartNew(); var querySamples = new List<double>(); var uiSamples = new List<double>(); var realized = 0;
            for (var i = 0; i < 24; i++)
            {
                var query = i % 2 == 0 ? "Performance" : "Музыка"; var offset = (i * 4099) % 99900;
                var queryTimer = Stopwatch.StartNew(); var page = await model.LibraryIndex.SearchAsync(query, offset); queryTimer.Stop(); querySamples.Add(queryTimer.Elapsed.TotalMilliseconds);
                if (page.Total != 100000 || page.Files.Length != 100 || page.Offset != offset) throw new InvalidOperationException("Production search lost paging/count at 100k rows.");
                var uiTimer = Stopwatch.StartNew(); var rendered = await window.LoadPageAsync(query, offset); window.UpdateLayout();
                await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); uiTimer.Stop(); uiSamples.Add(uiTimer.Elapsed.TotalMilliseconds);
                if (rendered is null || rendered.Total != 100000 || window.ResultList.Items.Count != 100 || rendered.Files[0].Id != page.Files[0].Id) throw new InvalidOperationException("Rendered library page lost rows/identity.");
                realized = Math.Max(realized, Containers(window.ResultList));
            }
            timer.Stop(); process.Refresh(); var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / timer.Elapsed.TotalMilliseconds * 100;
            if (realized is <= 0 or >= 100) throw new InvalidOperationException("Library page virtualization failed.");
            var drive = new DriveInfo(Path.GetPathRoot(directory)!);
            var physicalMemoryAvailable = GetPhysicallyInstalledSystemMemory(out var memoryKiB);
            return new { Status = "performance-measured", Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                CpuModel = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null)?.ToString()?.Trim() ?? "unavailable",
                PhysicalMemoryBytes = physicalMemoryAvailable ? (ulong?)(memoryKiB * 1024) : null, Storage = new { drive.DriveType, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace, PhysicalSsd = "not-established" },
                LogicalProcessors = Environment.ProcessorCount, DatasetRows = 100000, Dataset = "Owned synthetic SQLite metadata; no 100k physical music files or filesystem scan", SeedMilliseconds = seed.Elapsed.TotalMilliseconds,
                State = "Warm: Loaded query and serialized warmup drained; no forced GC", Queries = Timings(querySamples, 250), QueryAndUi = Timings(uiSamples, 250),
                UiMethod = "Production LoadPageAsync + UpdateLayout + Dispatcher ContextIdle; excludes 200 ms typing debounce", MaximumRealizedContainers = realized,
                MeasurementMilliseconds = timer.Elapsed.TotalMilliseconds, ProcessCpuPercentOfOneCore = cpu, ProcessCpuPercentOfAllLogicalProcessors = cpu / Environment.ProcessorCount,
                WorkingBytesBefore = workingBefore, WorkingBytesAfter = process.WorkingSet64, PrivateBytesBefore = privateBefore, PrivateBytesAfter = process.PrivateMemorySize64,
                LargeLibraryWorkingSetTargetBytes = 450L * 1024 * 1024, EndWorkingSetWithinTarget = process.WorkingSet64 < 450L * 1024 * 1024,
                HandlesBefore = handlesBefore, HandlesAfter = process.HandleCount, Acceptance = "Hosted Windows measurement; Windows 11 reference SSD/device/two-hour acceptance remains open" };
        }
        finally { window?.Close(); await model.DisposeWithoutSavingAsync(); Directory.Delete(directory, true); }
    }
}
