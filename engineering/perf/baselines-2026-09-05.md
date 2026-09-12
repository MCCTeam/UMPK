# Frame-path baselines, 2026-09-05

Recorded before any of the frame path moved, so a later run has something to be compared against.

## Run conditions

```
BenchmarkDotNet v0.15.8, Linux Arch Linux
AMD Ryzen 7 9800X3D, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.111
  [Host] : .NET 10.0.11, X64 RyuJIT x86-64-v4
  Job    : .NET 10.0.11, X64 RyuJIT x86-64-v4
Runtime=.NET 10.0  LaunchCount=1  WarmupCount=3  IterationCount=5
```

This is the SHORT job, not BenchmarkDotNet's default: one launch, three warmup iterations and five measured ones, which brought all 57 cases in at 10 min 25 s. The intervals below are what that job produced; `Error` is half of the 99.9% confidence interval. Treat a later change as real only when the intervals do not overlap, and re-run on this machine with nothing else on it.

Two `VarIntBenchmark` rows carry BenchmarkDotNet's own `ZeroMeasurement` warning: `ReadFromSpan` and `Write` at width 1 are indistinguishable from an empty method. Read them as "free", not as 0.19 ns.

## What the numbers say

**The identity half of frame dispatch costs more than the decode, for a small packet.** `DispatchOneFrame` is 31 to 35 ns; `DecodeOnly` is 8 to 11 ns. The difference, 21 to 25 ns, is the four identity queries and the two extra frame contexts around them. On a keep-alive that is three times the decode itself, and a keep-alive is the shape most inbound frames have.

**The identity queries are flat in table size.** `ResolveAcrossTheTable` divided by the table it swept gives 17.9 ns/id on 47 (74 entries), 18.5 on 393 (86), 19.1 on 770 (131) and 18.3 on 776 (141). Dictionary and array lookups, as expected, and the cost is per query rather than per entry: a bigger catalog does not make it worse, and a smaller one does not make it better.

**The per-frame copy is the whole cost of a large frame's hand-off.** At 64 KB, rent-copy-return is 1,648 ns and 65,560 B against 582 ns and nothing for the same rental without the copy. At 64 B the copy costs 1.7 ns and 88 B. The copy is deliberate (a frame crosses a bounded channel and must outlive the read loop's pooled buffer), and this is what it is worth.

**Chunk decode dominates everything else on the inbound path**, at 1.4 to 52 μs and 29 to 103 KB per column outside protocol 393. Protocol 393 is an outlier by two orders of magnitude: 1,403 μs and 291 KB for a 32 KB body, where 776 decodes a 44 KB body in 13 μs. The 1.13 column is built through the pre-flattening block-grid path; why that path costs a hundred times its successors is not measured here, and it is the first thing to look at for anyone optimising inbound wall time. The dispatch sequence is not.

**Descriptor build is a startup cost, not a per-frame one:** 21 μs and 50 KB on 47, 47 μs and 115 KB on 776, once per protocol per process.

## FrameDispatchBenchmark

The per-frame resolve-plus-decode sequence, over real recorded frames. `DispatchOneFrame` is the whole sequence; `DecodeOnly` is the decode alone, so the identity half reads as the difference; `ResolveAcrossTheTable` sweeps synthetic wire ids across the entire play clientbound table with no payload. The corpus carries no clientbound `move_entity_pos` for protocol 47, so that pairing is absent rather than authored.

| Method                | Case                | Mean         | Error      | StdDev    | Gen0   | Allocated |
|---------------------- |-------------------- |-------------:|-----------:|----------:|-------:|----------:|
| DispatchOneFrame      | 393/keep_alive      |    32.803 ns |  5.1701 ns | 1.3427 ns | 0.0005 |      24 B |
| DecodeOnly            | 393/keep_alive      |     7.671 ns |  0.3737 ns | 0.0971 ns | 0.0005 |      24 B |
| ResolveAcrossTheTable | 393/keep_alive      | 1,588.856 ns |  5.1762 ns | 1.3442 ns |      - |         - |
| DispatchOneFrame      | 393/move_entity_pos |    32.181 ns |  0.2231 ns | 0.0579 ns | 0.0006 |      32 B |
| DecodeOnly            | 393/move_entity_pos |    10.735 ns |  0.2370 ns | 0.0616 ns | 0.0006 |      32 B |
| ResolveAcrossTheTable | 393/move_entity_pos | 1,517.887 ns |  9.6708 ns | 2.5115 ns |      - |         - |
| DispatchOneFrame      | 47/keep_alive       |    34.532 ns |  0.6583 ns | 0.1709 ns | 0.0005 |      24 B |
| DecodeOnly            | 47/keep_alive       |     9.294 ns |  0.2066 ns | 0.0536 ns | 0.0005 |      24 B |
| ResolveAcrossTheTable | 47/keep_alive       | 1,321.450 ns | 34.1635 ns | 5.2868 ns |      - |         - |
| DispatchOneFrame      | 770/keep_alive      |    33.058 ns |  0.4500 ns | 0.1169 ns | 0.0005 |      24 B |
| DecodeOnly            | 770/keep_alive      |     7.698 ns |  0.1457 ns | 0.0378 ns | 0.0005 |      24 B |
| ResolveAcrossTheTable | 770/keep_alive      | 2,499.487 ns | 13.3831 ns | 3.4756 ns |      - |         - |
| DispatchOneFrame      | 770/move_entity_pos |    33.271 ns |  0.1281 ns | 0.0333 ns | 0.0006 |      32 B |
| DecodeOnly            | 770/move_entity_pos |    10.585 ns |  0.0400 ns | 0.0104 ns | 0.0006 |      32 B |
| ResolveAcrossTheTable | 770/move_entity_pos | 2,527.222 ns |  9.8438 ns | 1.5233 ns |      - |         - |
| DispatchOneFrame      | 776/keep_alive      |    30.926 ns |  0.1214 ns | 0.0188 ns | 0.0005 |      24 B |
| DecodeOnly            | 776/keep_alive      |     7.947 ns |  0.0357 ns | 0.0093 ns | 0.0005 |      24 B |
| ResolveAcrossTheTable | 776/keep_alive      | 2,583.689 ns | 12.6283 ns | 3.2795 ns |      - |         - |
| DispatchOneFrame      | 776/move_entity_pos |    33.474 ns |  0.0499 ns | 0.0130 ns | 0.0006 |      32 B |
| DecodeOnly            | 776/move_entity_pos |    10.628 ns |  0.1902 ns | 0.0494 ns | 0.0006 |      32 B |
| ResolveAcrossTheTable | 776/move_entity_pos | 2,576.157 ns | 16.3523 ns | 4.2466 ns |      - |         - |

## FrameCopyBenchmark

The rent, wire-id read, body copy and pool return the connection performs for every inbound frame.

| Method           | BodyBytes | Mean         | Error      | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |---------- |-------------:|-----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| RentCopyReturn   | 64        |    11.065 ns |  0.1069 ns |  0.0278 ns |  1.00 |    0.00 | 0.0017 |      88 B |        1.00 |
| ReuseWithoutCopy | 64        |     9.407 ns |  0.0375 ns |  0.0097 ns |  0.85 |    0.00 |      - |         - |        0.00 |
|                  |           |              |            |            |       |         |        |           |             |
| RentCopyReturn   | 1024      |    44.893 ns |  4.7713 ns |  1.2391 ns |  1.00 |    0.04 | 0.0209 |    1048 B |        1.00 |
| ReuseWithoutCopy | 1024      |    15.500 ns |  2.2288 ns |  0.3449 ns |  0.35 |    0.01 |      - |         - |        0.00 |
|                  |           |              |            |            |       |         |        |           |             |
| RentCopyReturn   | 65536     | 1,648.255 ns | 68.8671 ns | 10.6573 ns |  1.00 |    0.01 | 1.3008 |   65560 B |        1.00 |
| ReuseWithoutCopy | 65536     |   582.175 ns | 21.9698 ns |  5.7055 ns |  0.35 |    0.00 |      - |         - |        0.00 |

## ChunkDecodeBenchmark

One recorded `level_chunk_with_light` per chunk-codec era band. The 47 band is absent because the protocol-47 corpus carries `map_chunk_bulk` and no single chunk frame.

| Method      | Protocol | Mean         | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|------------ |--------- |-------------:|----------:|----------:|-------:|-------:|----------:|
| DecodeChunk | 107      |    51.746 μs | 0.4301 μs | 0.1117 μs | 2.0142 | 0.3662 | 100.36 KB |
| DecodeChunk | 393      | 1,403.245 μs | 4.4179 μs | 0.6837 μs | 5.8594 |      - |  290.8 KB |
| DecodeChunk | 477      |    58.236 μs | 1.2920 μs | 0.1999 μs | 0.6714 | 0.0610 |  33.22 KB |
| DecodeChunk | 573      |    46.908 μs | 2.8064 μs | 0.7288 μs | 0.7935 | 0.0610 |  39.22 KB |
| DecodeChunk | 735      |     1.400 μs | 0.1064 μs | 0.0276 μs | 0.6409 | 0.0324 |  31.43 KB |
| DecodeChunk | 751      |     3.689 μs | 0.0589 μs | 0.0091 μs | 0.6485 | 0.0114 |  31.92 KB |
| DecodeChunk | 755      |     4.248 μs | 0.3317 μs | 0.0861 μs | 0.6485 | 0.0153 |  31.96 KB |
| DecodeChunk | 758      |    10.684 μs | 0.3648 μs | 0.0565 μs | 0.6104 | 0.0305 |  30.52 KB |
| DecodeChunk | 764      |    10.332 μs | 0.3758 μs | 0.0582 μs | 0.5798 | 0.0305 |  29.07 KB |
| DecodeChunk | 768      |    14.817 μs | 0.4880 μs | 0.1267 μs | 1.7395 | 0.2899 |  85.83 KB |
| DecodeChunk | 770      |    22.378 μs | 0.4362 μs | 0.1133 μs | 1.9531 | 0.4883 |  96.97 KB |
| DecodeChunk | 776      |    13.444 μs | 0.6766 μs | 0.1757 μs | 2.0905 | 0.4120 | 102.93 KB |

## ItemStackDecodeBenchmark

A recorded `container_set_content` per component-era table. The recorded inventories are a player's own container as an offline server hands it over, so the stacks are mostly empty: this is per-slot framing cost, not a worst case for component payloads.

| Method                  | Protocol | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------------------------ |--------- |---------:|---------:|---------:|-------:|----------:|
| DecodeContainerContents | 766      | 98.97 ns | 3.363 ns | 0.873 ns | 0.0086 |     432 B |
| DecodeContainerContents | 770      | 91.39 ns | 4.115 ns | 0.637 ns | 0.0086 |     432 B |
| DecodeContainerContents | 776      | 93.33 ns | 2.093 ns | 0.544 ns | 0.0086 |     432 B |

## VarIntBenchmark

The VarInt readers on the wire today, so a later consolidation onto one body can be shown to change nothing per frame. The frame path already runs one implementation and it inlines.

| Method                     | Width | Mean       | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------------------------- |------ |-----------:|----------:|----------:|------:|--------:|----------:|------------:|
| ReadFromSpan               | 1     |  0.1921 ns | 0.0038 ns | 0.0006 ns |  1.00 |    0.00 |         - |          NA |
| ReadWireIdFromFrame        | 1     |  0.2249 ns | 0.0284 ns | 0.0044 ns |  1.17 |    0.02 |         - |          NA |
| ReadFromPacketReader       | 1     |  0.8884 ns | 0.0050 ns | 0.0008 ns |  4.63 |    0.01 |         - |          NA |
| ReadFromContiguousSequence | 1     |  3.1371 ns | 0.0133 ns | 0.0021 ns | 16.33 |    0.05 |         - |          NA |
| ReadFromSplitSequence      | 1     |  7.1172 ns | 0.0472 ns | 0.0073 ns | 37.05 |    0.11 |         - |          NA |
| Write                      | 1     |  0.1915 ns | 0.0096 ns | 0.0015 ns |  1.00 |    0.01 |         - |          NA |
|                            |       |            |           |           |       |         |           |             |
| ReadFromSpan               | 5     |  1.5529 ns | 0.0619 ns | 0.0161 ns |  1.00 |    0.01 |         - |          NA |
| ReadWireIdFromFrame        | 5     |  1.6387 ns | 0.0120 ns | 0.0031 ns |  1.06 |    0.01 |         - |          NA |
| ReadFromPacketReader       | 5     |  3.6196 ns | 0.0500 ns | 0.0130 ns |  2.33 |    0.02 |         - |          NA |
| ReadFromContiguousSequence | 5     |  5.8534 ns | 0.0987 ns | 0.0256 ns |  3.77 |    0.04 |         - |          NA |
| ReadFromSplitSequence      | 5     | 10.3957 ns | 0.0795 ns | 0.0123 ns |  6.69 |    0.06 |         - |          NA |
| Write                      | 5     |  1.3725 ns | 0.0148 ns | 0.0038 ns |  0.88 |    0.01 |         - |          NA |

## DescriptorBuildBenchmark

One cold descriptor build, through the generated builders rather than the memoising accessors.

| Method          | Protocol | Mean     | Error    | StdDev   | Gen0   | Gen1   | Allocated |
|---------------- |--------- |---------:|---------:|---------:|-------:|-------:|----------:|
| BuildDescriptor | 47       | 21.43 μs | 0.970 μs | 0.252 μs | 1.0071 | 0.1221 |  50.41 KB |
| BuildDescriptor | 766      | 41.37 μs | 0.793 μs | 0.206 μs | 1.9531 | 0.4272 |  95.83 KB |
| BuildDescriptor | 776      | 47.23 μs | 1.186 μs | 0.308 μs | 2.3193 | 0.6104 | 115.09 KB |
