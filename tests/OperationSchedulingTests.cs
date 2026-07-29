using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

/// <summary>
/// Which operations run at once (PRD §6.3). Two transfers on one physical disk do not go twice as
/// fast — they interleave and both go slower — so they queue; two on genuinely separate disks have
/// no reason to wait for each other.
/// </summary>
[TestFixture]
public class OperationSchedulingTests
{
    /// <summary>A transfer that never finishes until the test lets it, so overlap is observable.</summary>
    private sealed class HeldTransfers : IFileOperationService
    {
        private readonly Dictionary<string, TaskCompletionSource> _gates = new(StringComparer.Ordinal);
        private readonly List<string> _inFlight = [];
        private readonly Lock _lock = new();

        /// <summary>Sources currently inside TransferAsync.</summary>
        internal List<string> InFlight
        {
            get
            {
                lock (_lock)
                    return [.. _inFlight];
            }
        }

        internal void Release(string source)
        {
            TaskCompletionSource? gate;
            lock (_lock)
                _gates.TryGetValue(source, out gate);

            gate?.TrySetResult();
        }

        public async Task TransferAsync(
            IReadOnlyList<string> sources, string destinationDir, FileOperationKind kind,
            IProgress<OperationProgress>? progress,
            Func<ConflictRequest, ConflictResolution> conflictResolver,
            CancellationToken cancellationToken = default)
        {
            var key = sources[0];
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _inFlight.Add(key);
                _gates[key] = gate;
            }

            try
            {
                await gate.Task.ConfigureAwait(false);
            }
            finally
            {
                lock (_lock)
                    _inFlight.Remove(key);
            }
        }

        public Task<string> CreateFolderAsync(string parentDir, string name, CancellationToken ct = default)
            => Task.FromResult(Path.Combine(parentDir, name));

        public Task<string> CreateFileAsync(string parentDir, string name, CancellationToken ct = default)
            => Task.FromResult(Path.Combine(parentDir, name));

        public Task<string> RenameAsync(string path, string newName, CancellationToken ct = default)
            => Task.FromResult(newName);
    }

    /// <summary>A queue whose idea of "which disk is this" is a lookup the test controls.</summary>
    private static OperationQueueViewModel QueueOver(HeldTransfers service, Dictionary<string, string> devices)
        => new(service) { DeviceOf = path => devices.TryGetValue(path, out var d) ? d : string.Empty };

    /// <summary>Waits for the queue to reach a state, rather than guessing how many yields it takes —
    /// an operation finishing hands off through the thread pool.</summary>
    private static async Task UntilAsync(Func<bool> settled, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (settled())
                return;

            await Task.Delay(5);
        }

        Assert.Fail($"timed out waiting for {what}");
    }

    /// <summary>Waits until the set of running transfers is exactly this, and stays there.</summary>
    private static async Task RunningAsync(HeldTransfers service, params string[] expected)
    {
        await UntilAsync(() => service.InFlight.Count == expected.Length, $"{expected.Length} running");
        await Task.Delay(30); // nothing else may sneak in behind it
        Assert.That(service.InFlight, Is.EquivalentTo(expected));
    }

    [Test]
    public async Task Two_Transfers_On_Different_Disks_Run_At_The_Same_Time()
    {
        var service = new HeldTransfers();
        var queue = QueueOver(service, new()
        {
            ["/mnt/a/one"] = "/dev/sda",
            ["/mnt/a"] = "/dev/sda",
            ["/mnt/b/two"] = "/dev/sdb",
            ["/mnt/b"] = "/dev/sdb",
        });

        queue.Enqueue(FileOperationKind.Copy, ["/mnt/a/one"], "/mnt/a");
        queue.Enqueue(FileOperationKind.Copy, ["/mnt/b/two"], "/mnt/b");

        await RunningAsync(service, "/mnt/a/one", "/mnt/b/two");
    }

    [Test]
    public async Task Two_Transfers_On_One_Disk_Take_Turns()
    {
        var service = new HeldTransfers();
        var queue = QueueOver(service, new()
        {
            ["/mnt/a/one"] = "/dev/sda",
            ["/mnt/a/two"] = "/dev/sda",
            ["/mnt/a"] = "/dev/sda",
        });

        queue.Enqueue(FileOperationKind.Copy, ["/mnt/a/one"], "/mnt/a");
        var second = queue.Enqueue(FileOperationKind.Copy, ["/mnt/a/two"], "/mnt/a");

        await RunningAsync(service, "/mnt/a/one");
        Assert.That(second.Status, Is.EqualTo(OperationStatus.Pending), "only the first started");

        service.Release("/mnt/a/one");

        await RunningAsync(service, "/mnt/a/two");
    }

    [Test]
    public async Task Partitions_Of_One_Disk_Count_As_One_Disk()
    {
        // The whole reason the scheduler asks for a physical device rather than a mount point: two
        // mounts on one spindle are one set of heads however separate the paths look.
        var service = new HeldTransfers();
        var queue = QueueOver(service, new()
        {
            ["/mnt/part1/x"] = "/dev/sda",
            ["/mnt/part1"] = "/dev/sda",
            ["/mnt/part2/y"] = "/dev/sda",
            ["/mnt/part2"] = "/dev/sda",
        });

        queue.Enqueue(FileOperationKind.Copy, ["/mnt/part1/x"], "/mnt/part1");
        queue.Enqueue(FileOperationKind.Copy, ["/mnt/part2/y"], "/mnt/part2");

        await RunningAsync(service, "/mnt/part1/x");
    }

    [Test]
    public async Task A_Transfer_Between_Two_Disks_Blocks_Both_Of_Them()
    {
        var service = new HeldTransfers();
        var queue = QueueOver(service, new()
        {
            ["/mnt/a/one"] = "/dev/sda",
            ["/mnt/b"] = "/dev/sdb",
            ["/mnt/b/two"] = "/dev/sdb",
        });

        queue.Enqueue(FileOperationKind.Copy, ["/mnt/a/one"], "/mnt/b"); // reads sda, writes sdb
        queue.Enqueue(FileOperationKind.Copy, ["/mnt/b/two"], "/mnt/b"); // wants sdb

        await RunningAsync(service, "/mnt/a/one");
    }

    [Test]
    public async Task A_Disk_That_Cannot_Be_Identified_Is_Assumed_To_Clash()
    {
        // Guessing wrong this way costs some parallelism; guessing the other way thrashes the very
        // hardware the rule exists to protect.
        var service = new HeldTransfers();
        var queue = QueueOver(service, []);

        queue.Enqueue(FileOperationKind.Copy, ["/unknown/one"], "/unknown");
        queue.Enqueue(FileOperationKind.Copy, ["/elsewhere/two"], "/elsewhere");

        await RunningAsync(service, "/unknown/one");
    }

    [Test]
    public async Task However_Many_Disks_Are_Free_The_Ceiling_Holds()
    {
        var service = new HeldTransfers();
        var devices = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < 10; ++i)
        {
            devices[$"/mnt/{i}/f"] = $"/dev/sd{i}";
            devices[$"/mnt/{i}"] = $"/dev/sd{i}";
        }

        var queue = QueueOver(service, devices);
        queue.MaxConcurrent = 3;

        for (var i = 0; i < 10; ++i)
            queue.Enqueue(FileOperationKind.Copy, [$"/mnt/{i}/f"], $"/mnt/{i}");

        await RunningAsync(service, "/mnt/0/f", "/mnt/1/f", "/mnt/2/f");
    }

    [Test]
    public async Task A_Later_Operation_On_A_Free_Disk_Does_Not_Wait_Behind_A_Blocked_One()
    {
        // Head-of-line blocking would make the whole feature pointless: one slow disk would stall
        // transfers that have nothing to do with it.
        var service = new HeldTransfers();
        var queue = QueueOver(service, new()
        {
            ["/mnt/a/one"] = "/dev/sda",
            ["/mnt/a/two"] = "/dev/sda",
            ["/mnt/a"] = "/dev/sda",
            ["/mnt/b/three"] = "/dev/sdb",
            ["/mnt/b"] = "/dev/sdb",
        });

        queue.Enqueue(FileOperationKind.Copy, ["/mnt/a/one"], "/mnt/a");   // runs
        queue.Enqueue(FileOperationKind.Copy, ["/mnt/a/two"], "/mnt/a");   // blocked behind it
        queue.Enqueue(FileOperationKind.Copy, ["/mnt/b/three"], "/mnt/b"); // unrelated disk

        await RunningAsync(service, "/mnt/a/one", "/mnt/b/three");
    }
}
