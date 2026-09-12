using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Umpk.Protocol.Java;

namespace Umpk.Benchmarks;

/// <summary>One descriptor build, which is what a session pays once per connect and what a host pays for every version it offers.</summary>
/// <remarks>The shipped accessors memoise, so this calls the generated builders directly to measure a cold build. The shared binding table is forced in setup, because it is built once per process and would otherwise land inside the first measured build instead of beside it.</remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class DescriptorBuildBenchmark
{
    [Params(47, 766, 776)]
    public int Protocol { get; set; }

    [GlobalSetup]
    public void Setup() => _ = Build();

    [Benchmark]
    public ProtocolDescriptor BuildDescriptor() => Build();

    private JavaVersion BuildVersion() => Protocol switch
    {
        47 => Umpk.Data.Java.V47.Descriptor.Build(),
        766 => Umpk.Data.Java.V766.Descriptor.Build(),
        776 => Umpk.Data.Java.V776.Descriptor.Build(),
        _ => throw new InvalidOperationException($"No builder wired for protocol {Protocol}."),
    };

    private ProtocolDescriptor Build() => BuildVersion().Protocol;
}
