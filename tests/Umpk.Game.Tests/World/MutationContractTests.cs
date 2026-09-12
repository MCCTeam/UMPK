using System.Diagnostics;
using System.Threading;
using Umpk.Game.World;
using Xunit;

namespace Umpk.Game.Tests.World;

/// <summary>
/// The section mutation contract: a writer on the session loop hammers sections with in-place sets while an off-loop reader reads the same cells continuously. The reader must never see a torn value; it may see either the old or the new value, but only ever a value the writer actually wrote. Threads (not Tasks) are used so the test blocks on join without the async-blocking analyzer complaint, matching the "writer thread vs reader thread" model.
///
/// <para>The writer cycles through short-lived sections, so every encoding transition (single-value promotion, 1/2/3-bit biome-style widening, indirect widening, direct promotion, direct re-widening) happens over and over for the whole test window instead of only in the first milliseconds; a minimum-reads and minimum-cycles assertion rejects a starved reader or writer that would otherwise green the test vacuously.</para>
/// </summary>
public class MutationContractTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(2);

    // Floors chosen orders of magnitude below observed throughput but high enough that a stalled thread cannot pass: a starved reader doing only occasional reads fails the floor.
    private const long MinReads = 50_000;
    private const int MinWriterCycles = 3;

    [Fact]
    public void ConcurrentReadWrite_GrowthUnderReader_NeverSeesTornValue()
    {
        // Values span a range wide enough to force palette growth all the way to direct storage (257+ distinct ids) plus a final wide id that forces a direct re-widen.
        int[] values = new int[300];
        for (int i = 0; i < values.Length; i++)
            values[i] = 1000 + i * 7;

        values[^1] = 1_000_000; // forces the direct re-widen transition every cycle

        var allowed = new HashSet<int>(values) { 0 };
        int x = 1, y = 2, z = 3;

        ChunkSection shared = ChunkSection.Filled(0);
        long writerCycles = 0;
        long reads = 0;
        Exception? readerFailure = null;
        Exception? writerFailure = null;
        using var cts = new CancellationTokenSource();

        var writer = new Thread(() =>
        {
            try
            {
                var clock = Stopwatch.StartNew();
                while (!cts.IsCancellationRequested && clock.Elapsed < Duration)
                {
                    // A fresh section per cycle: the reader keeps observing single->indirect->direct promotions and re-widens for the entire window, not just at the start.
                    var section = ChunkSection.Filled(0);
                    Volatile.Write(ref shared, section);
                    foreach (int value in values)
                        section.SetBlockStateId(x, y, z, value);

                    Interlocked.Increment(ref writerCycles);
                }

                cts.Cancel();
            }
            catch (Exception ex)
            {
                writerFailure = ex;
                cts.Cancel();
            }
        });

        var reader = new Thread(() =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    ChunkSection current = Volatile.Read(ref shared);
                    int v = current.GetBlockStateId(x, y, z);
                    if (!allowed.Contains(v))
                        throw new Xunit.Sdk.XunitException(
                            $"Reader observed torn/unexpected value {v} after {Interlocked.Read(ref reads)} reads.");

                    Interlocked.Increment(ref reads);
                }
            }
            catch (Exception ex)
            {
                readerFailure = ex;
                cts.Cancel();
            }
        });

        writer.Start();
        reader.Start();
        Assert.True(writer.Join(TimeSpan.FromSeconds(30)), "writer did not finish");
        Assert.True(reader.Join(TimeSpan.FromSeconds(30)), "reader did not finish");

        Assert.Null(writerFailure);
        Assert.Null(readerFailure);
        Assert.True(Interlocked.Read(ref reads) >= MinReads, $"reader was starved: only {Interlocked.Read(ref reads)} reads");
        Assert.True(Interlocked.Read(ref writerCycles) >= MinWriterCycles, $"writer completed only {Interlocked.Read(ref writerCycles)} growth cycles");
    }

    [Fact]
    public void ConcurrentWholeSection_ReadsStayConsistentPerCell()
    {
        // Whole-section sweeps against a scanning reader. Every read of any cell must be either the initial 0 or the value the writer legitimately assigns to that cell; each sweep runs on a fresh section, so the single->indirect->direct ladder replays under the reader all test long.
        ChunkSection shared = ChunkSection.Filled(0);
        long writerCycles = 0;
        long reads = 0;
        Exception? failure = null;
        Exception? writerFailure = null;
        using var cts = new CancellationTokenSource();

        static int CellValue(int i) => (i + 1) * 2;

        var writer = new Thread(() =>
        {
            try
            {
                var clock = Stopwatch.StartNew();
                while (!cts.IsCancellationRequested && clock.Elapsed < Duration)
                {
                    var section = ChunkSection.Filled(0);
                    Volatile.Write(ref shared, section);
                    for (int i = 0; i < ChunkSection.BlockCells; i++)
                    {
                        int lx = i & 15, lz = (i >> 4) & 15, ly = i >> 8;
                        section.SetBlockStateId(lx, ly, lz, CellValue(i));
                    }

                    Interlocked.Increment(ref writerCycles);
                }

                cts.Cancel();
            }
            catch (Exception ex)
            {
                writerFailure = ex;
                cts.Cancel();
            }
        });

        var reader = new Thread(() =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    ChunkSection current = Volatile.Read(ref shared);
                    for (int i = 0; i < ChunkSection.BlockCells; i++)
                    {
                        int lx = i & 15, lz = (i >> 4) & 15, ly = i >> 8;
                        int v = current.GetBlockStateId(lx, ly, lz);
                        if (v != 0 && v != CellValue(i))
                            throw new Xunit.Sdk.XunitException($"Cell {i} read {v}, expected 0 or {CellValue(i)}.");

                        Interlocked.Increment(ref reads);
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                cts.Cancel();
            }
        });

        writer.Start();
        reader.Start();
        Assert.True(writer.Join(TimeSpan.FromSeconds(30)), "writer did not finish");
        Assert.True(reader.Join(TimeSpan.FromSeconds(30)), "reader did not finish");

        Assert.Null(writerFailure);
        Assert.Null(failure);
        Assert.True(Interlocked.Read(ref reads) >= MinReads, $"reader was starved: only {Interlocked.Read(ref reads)} reads");
        Assert.True(Interlocked.Read(ref writerCycles) >= MinWriterCycles, $"writer completed only {Interlocked.Read(ref writerCycles)} sweeps");
    }
}
