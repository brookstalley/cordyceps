using System;
using System.Threading;
using System.Threading.Tasks;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

/// <summary>
/// Tests for <see cref="LogBuffer"/> — the host-free ring buffer behind DebugLog
/// (which wraps a static instance and owns the RhinoApp.WriteLine emission).
/// </summary>
public class LogBufferTests
{
    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogBuffer(0));
    }

    [Fact]
    public void AddingExactlyMaxEntries_KeepsAllOfThem()
    {
        var buffer = new LogBuffer(5);
        for (int i = 0; i < 5; i++)
            buffer.Add($"m{i}", "INFO", 1);

        var entries = buffer.GetEntries();
        Assert.Equal(5, buffer.Count);
        Assert.Equal("m0", entries[0].Message); // nothing trimmed at exactly capacity
        Assert.Equal("m4", entries[4].Message);
    }

    [Fact]
    public void AddingMaxEntriesPlusOne_DropsOnlyTheOldest()
    {
        var buffer = new LogBuffer(5);
        for (int i = 0; i < 6; i++)
            buffer.Add($"m{i}", "INFO", 1);

        var entries = buffer.GetEntries();
        Assert.Equal(5, buffer.Count);
        Assert.Equal("m1", entries[0].Message); // m0 trimmed
        Assert.Equal("m5", entries[4].Message);
    }

    [Fact]
    public void Add_StoresLevelAndMessage()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("boom", "ERROR", 0);

        var entry = Assert.Single(buffer.GetEntries());
        Assert.Equal("ERROR", entry.Level);
        Assert.Equal("boom", entry.Message);
    }

    [Fact]
    public void Add_GatesConsoleEmissionByLevel_ButAlwaysBuffers()
    {
        var buffer = new LogBuffer(10) { DebugLevel = 0 };

        // messageLevel 1 > DebugLevel 0: buffered but not emitted
        Assert.False(buffer.Add("chatty", "INFO", 1));
        // messageLevel 0 <= DebugLevel 0: buffered and emitted (errors use level 0)
        Assert.True(buffer.Add("boom", "ERROR", 0));

        Assert.Equal(2, buffer.Count); // gating never drops entries from the buffer

        buffer.DebugLevel = 1;
        Assert.True(buffer.Add("chatty again", "INFO", 1));
        Assert.False(buffer.Add("very verbose", "DEBUG", 2));
    }

    [Fact]
    public void GetEntriesSince_FiltersByTimestampInclusive()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("early", "INFO", 1);
        Thread.Sleep(30); // separate the two timestamps beyond clock granularity
        buffer.Add("late", "INFO", 1);
        var lateStamp = buffer.GetEntries()[1].Timestamp;

        Assert.Equal(2, buffer.GetEntriesSince(DateTime.MinValue).Count);
        Assert.Empty(buffer.GetEntriesSince(DateTime.UtcNow.AddMinutes(1)));

        var since = buffer.GetEntriesSince(lateStamp); // >= is inclusive of the boundary entry
        var entry = Assert.Single(since);
        Assert.Equal("late", entry.Message);
    }

    [Fact]
    public void Clear_EmptiesTheBuffer()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("a", "INFO", 1);
        buffer.Add("b", "INFO", 1);

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.GetEntries());
    }

    [Fact]
    public void GetEntries_WithClear_ReturnsSnapshotAndEmptiesBuffer()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("a", "INFO", 1);

        var snapshot = buffer.GetEntries(clear: true);

        Assert.Single(snapshot);
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public async Task ParallelAdds_LoseNoEntries()
    {
        // Thread-safety smoke: concurrent writers (HTTP workers + UI thread in production)
        // must not lose or corrupt entries. Capacity exceeds the total so no trimming occurs
        // and the final count is exact.
        const int writers = 8;
        const int perWriter = 1000;
        var buffer = new LogBuffer(writers * perWriter + 1);

        var tasks = new Task[writers];
        for (int t = 0; t < writers; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < perWriter; i++)
                    buffer.Add("entry", "INFO", 1);
            });
        }
        await Task.WhenAll(tasks);

        Assert.Equal(writers * perWriter, buffer.Count);
    }
}
