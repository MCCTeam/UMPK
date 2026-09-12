using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit.Corpus;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Protocol.Java.Tests.Performance;

/// <summary>Bytes allocated per decode, per corpus-covered packet family, against the checked-in allocation budgets.</summary>
/// <remarks>
/// <para>This is deterministic (allocation, not time), runs in milliseconds per row, and fails on the exact regression class a refactor of the decode path can introduce, such as adding a <c>ToArray()</c>. Time has no gate here because it cannot be enforced consistently on an unpinned workstation; benchmarks report timing intervals separately.</para>
/// <para>The measurement is the BODY after the wire id, not the frame, because that is what <see cref="BoundPacketCodec.Decode"/> takes. Registries are the production ones, so the number is comparable with the conformance suite's; under an empty registry view an item field takes the lookup-miss branch and the measurement is not the production allocation.</para>
/// <para>Updating a budget requires reviewing each changed row as either an intentional model cost or an allocation regression.</para>
/// </remarks>
public sealed class AllocationBudgetTests
{
    private readonly ITestOutputHelper _output;

    public AllocationBudgetTests(ITestOutputHelper output) => _output = output;

    /// <summary>One row per pinned family. The sentinel empty key keeps the theory non-empty before the file has ever been written, so the first run reports the missing pin instead of an empty theory.</summary>
    public static TheoryData<string> Budgets
    {
        get
        {
            var data = new TheoryData<string>();
            IReadOnlyList<AllocationBudget> rows = AllocationBudgets.File?.Budgets ?? [];
            if (rows.Count == 0)
            {
                data.Add(string.Empty);
                return data;
            }

            foreach (AllocationBudget row in rows)
                data.Add(row.Key);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Budgets))]
    public void DecodeStaysWithinItsAllocationBudget(string key)
    {
        Assert.True(
            key.Length > 0,
            $"No allocation budgets pinned at {AllocationBudgets.Path} " +
            $"(set {AllocationBudgets.UpdateVariable}=1 to seed them).");

        AllocationBudget row = AllocationBudgets.Get(key)
            ?? throw new InvalidOperationException($"Budget row '{key}' vanished between discovery and run.");

        long perFrame = MeasureBytesPerDecode(ToReference(row));
        _output.WriteLine($"{key}: {perFrame} B/frame, budget {row.BudgetBytesPerFrame} B, pinned at {row.MeasuredBytesPerFrame} B.");

        Assert.True(
            perFrame <= row.BudgetBytesPerFrame,
            $"{row.Packet} on protocol {row.Protocol} allocated {perFrame} B/frame, budget {row.BudgetBytesPerFrame} B.");
    }

    /// <summary>The pinned rows must be exactly the families the corpus covers, so a capture that lands later brings its family into the gate instead of silently sitting outside it.</summary>
    [Fact]
    public void PinnedBudgets_AreExactlyTheCorpusCoveredFamilies()
    {
        List<CorpusFrameRef> resolved = [.. CorpusFrames.EnumerateCoveredBindings()];
        IReadOnlyList<CorpusFrameRef> covered = CorpusFrames.SelectOnePerCoveredFamily(resolved);
        if (covered.Count == 0)
        {
            _output.WriteLine("No corpora present; the allocation gate has nothing to cover.");
            return;
        }

        (int implemented, int exercised) = CountBindingCoverage(resolved);
        _output.WriteLine(
            $"corpus-covered families: {covered.Count}; " +
            $"exercised bindings {exercised} of {implemented} implemented " +
            $"({exercised * 100.0 / implemented:0.0}%).");

        if (Environment.GetEnvironmentVariable(AllocationBudgets.UpdateVariable) == "1")
        {
            var file = new AllocationBudgetFile
            {
                ImplementedBindings = implemented,
                ExercisedBindings = exercised,
                CoveredFamilies = covered.Count,
            };

            foreach (CorpusFrameRef reference in covered)
            {
                long measured = MeasureBytesPerDecode(reference);
                file.Budgets.Add(new AllocationBudget
                {
                    Protocol = reference.Protocol,
                    Phase = reference.Phase.ToString(),
                    Flow = reference.Flow.ToString(),
                    Packet = reference.Packet,
                    Capture = reference.Capture,
                    Frame = reference.Frame,
                    WireId = reference.WireId,
                    BodyBytes = reference.BodyLength,
                    MeasuredBytesPerFrame = measured,
                    BudgetBytesPerFrame = AllocationBudgets.WithHeadroom(measured),
                });
            }

            AllocationBudgets.Write(file);
            _output.WriteLine($"Re-pinned {file.Budgets.Count} budgets at {AllocationBudgets.Path}.");
        }

        AllocationBudgetFile? pinned = AllocationBudgets.File;
        Assert.True(
            pinned is not null,
            $"No allocation budgets pinned at {AllocationBudgets.Path} " +
            $"(set {AllocationBudgets.UpdateVariable}=1 to seed them).");

        string[] expected = [.. covered.Select(r => AllocationBudget.Format(r.Protocol, r.Phase.ToString(), r.Flow.ToString(), r.Packet))];
        string[] actual = [.. pinned!.Budgets.Select(b => b.Key)];
        Assert.Equal(expected, actual);
    }

    /// <summary>Bytes allocated by one decode of the referenced frame. The warm loop has the same shape as the measured one rather than a single call, so a table built lazily on first use is built before the window opens instead of inside it.</summary>
    private static long MeasureBytesPerDecode(CorpusFrameRef reference)
    {
        (BoundPacketCodec binding, byte[] body) = CorpusFrames.Load(reference);
        var context = new PacketCodecContext(JavaGameData.Registries(reference.Protocol), IConnectionCodecState.Empty);
        int iterations = IterationsFor(body.Length);

        for (int i = 0; i < iterations; i++)
            _ = binding.Decode(body, context);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            _ = binding.Decode(body, context);

        // Integer division floors sub-byte regressions, which is acceptable for a gate whose purpose is catching a reintroduced copy rather than costing a single byte.
        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    /// <summary>A thousand iterations for an ordinary body, a hundred for a chunk-sized one. The fast allocation counter can lag by up to one allocation context (8 KB), so the divisor sets the noise floor: 8 B/frame at a thousand, 80 B/frame at a hundred, which is far below what any frame this large allocates and keeps the suite's runtime in seconds rather than minutes.</summary>
    private static int IterationsFor(int bodyLength) => bodyLength <= 8192 ? 1_000 : 100;

    private static CorpusFrameRef ToReference(AllocationBudget row) => new(
        row.Protocol,
        row.Capture,
        row.Frame,
        Enum.Parse<ProtocolPhase>(row.Phase),
        Enum.Parse<PacketFlow>(row.Flow),
        row.WireId,
        row.Packet,
        row.BodyBytes);

    /// <summary>How many implemented bindings the catalog holds and how many of them a corpus frame reaches, as distinct (protocol, phase, flow, wire id) tuples. This is the covered fraction the gate's reach is bounded by.</summary>
    private static (int Implemented, int Exercised) CountBindingCoverage(IEnumerable<CorpusFrameRef> resolved)
    {
        var implemented = new HashSet<(int, ProtocolPhase, PacketFlow, int)>();
        foreach (JavaVersion version in JavaVersions.All)
        {
            int protocol = version.Version.Protocol;
            foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
                foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
                {
                    if (!version.Protocol.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                        continue;

                    foreach ((int wireId, PacketType _) in registry.Packets)
                        if (registry.TryGetInbound(wireId, out BoundPacketCodec entry) && entry.IsImplemented)
                            implemented.Add((protocol, phase, flow, wireId));

                }

        }

        var exercised = new HashSet<(int, ProtocolPhase, PacketFlow, int)>();
        foreach (CorpusFrameRef reference in resolved)
        {
            var key = (reference.Protocol, reference.Phase, reference.Flow, reference.WireId);
            if (implemented.Contains(key))
                exercised.Add(key);

        }

        return (implemented.Count, exercised.Count);
    }
}
