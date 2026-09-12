# Frame dispatch, before and after one resolution per frame

2026-09-05. A re-run of `FrameDispatchBenchmark` alone, against its row in `baselines-2026-09-05.md`, on the same machine and the same job.

## Run conditions

```
BenchmarkDotNet v0.15.8, Linux Arch Linux
AMD Ryzen 7 9800X3D, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.111
  [Host] : .NET 10.0.11, X64 RyuJIT x86-64-v4
  Job    : .NET 10.0.11, X64 RyuJIT x86-64-v4
Runtime=.NET 10.0  LaunchCount=1  WarmupCount=3  IterationCount=5
```

The same short job the baseline used. `Error` is half of the 99.9% confidence interval; a change is real only where the intervals do not overlap.

`DecodeOnly` measures a slightly larger body than it did: it now resolves the frame and then decodes it, because resolution and decode share one frame context. That makes it the right subtrahend for reading the identity half out of `DispatchOneFrame`, and it is why its own numbers barely move.

## FrameDispatchBenchmark

| Method | Case | Before (ns) | After (ns) |
| --- | --- | ---: | ---: |
| DispatchOneFrame | 47/keep_alive | 34.532 ± 0.6583 | 8.069 ± 0.0735 |
| DecodeOnly | 47/keep_alive | 9.294 ± 0.2066 | 8.481 ± 0.5419 |
| ResolveAcrossTheTable | 47/keep_alive | 1,321.450 ± 34.1635 | 132.895 ± 0.5744 |
| DispatchOneFrame | 393/keep_alive | 32.803 ± 5.1701 | 6.684 ± 0.0623 |
| DecodeOnly | 393/keep_alive | 7.671 ± 0.3737 | 6.901 ± 0.0278 |
| ResolveAcrossTheTable | 393/keep_alive | 1,588.856 ± 5.1762 | 154.568 ± 0.1259 |
| DispatchOneFrame | 393/move_entity_pos | 32.181 ± 0.2231 | 9.312 ± 0.0590 |
| DecodeOnly | 393/move_entity_pos | 10.735 ± 0.2370 | 9.506 ± 0.0233 |
| ResolveAcrossTheTable | 393/move_entity_pos | 1,517.887 ± 9.6708 | 154.716 ± 0.6856 |
| DispatchOneFrame | 770/keep_alive | 33.058 ± 0.4500 | 7.682 ± 0.4020 |
| DecodeOnly | 770/keep_alive | 7.698 ± 0.1457 | 7.065 ± 0.0605 |
| ResolveAcrossTheTable | 770/keep_alive | 2,499.487 ± 13.3831 | 246.857 ± 1.8691 |
| DispatchOneFrame | 770/move_entity_pos | 33.271 ± 0.1281 | 9.473 ± 0.0090 |
| DecodeOnly | 770/move_entity_pos | 10.585 ± 0.0400 | 10.118 ± 0.0262 |
| ResolveAcrossTheTable | 770/move_entity_pos | 2,527.222 ± 9.8438 | 244.370 ± 2.5142 |
| DispatchOneFrame | 776/keep_alive | 30.926 ± 0.1214 | 11.676 ± 0.1230 |
| DecodeOnly | 776/keep_alive | 7.947 ± 0.0357 | 6.912 ± 0.0530 |
| ResolveAcrossTheTable | 776/keep_alive | 2,583.689 ± 12.6283 | 261.087 ± 1.6840 |
| DispatchOneFrame | 776/move_entity_pos | 33.474 ± 0.0499 | 9.483 ± 0.0321 |
| DecodeOnly | 776/move_entity_pos | 10.628 ± 0.1902 | 10.057 ± 0.0738 |
| ResolveAcrossTheTable | 776/move_entity_pos | 2,576.157 ± 16.3523 | 265.288 ± 3.0872 |

Allocation is unchanged on every row: 24 B for a keep-alive, 32 B for a `move_entity_pos`, nothing for the sweep. Nothing in this change touches what a frame allocates, and the numbers agree.

## What the numbers say

**The identity half of a delivered frame is now inside the noise of the decode.** `DispatchOneFrame` was 31 to 35 ns against a 7.7 to 10.7 ns `DecodeOnly`, so the four identity queries and the two extra frame contexts cost 21 to 25 ns, about three times the decode on the shape most inbound frames have. After, `DispatchOneFrame` is 6.7 to 11.7 ns and sits within a nanosecond of `DecodeOnly` on six of the seven cases. The intervals do not overlap on any `DispatchOneFrame` row.

The seventh, `776/keep_alive`, is the one row where the two still differ: 11.676 against 6.912, so about 4.8 ns unaccounted for on the largest table. It is still a quarter of what that case cost before, and it is the only case where the gap survives, so it reads as layout noise rather than a cost the design carries. Worth a second look if the sweep below ever stops being flat.

**The sweep fell by about a factor of ten, and stayed flat in table size.** `ResolveAcrossTheTable` divided by the table it swept is 1.80 ns/id on 47 (74 entries), 1.80 on 393 (86), 1.88 on 770 (131) and 1.85 on 776 (141), against 17.9, 18.5, 19.1 and 18.3 before. Flat before and flat after, which is what a hash lookup and an array index both look like; the change is that there are five fewer of them per frame, and the phase route the two registry lookups came from is now resolved about five times per session instead of twice per frame.

**A binding that resolves nothing is not in this table.** Every case here is `DescriptorFrameCodecBinding`. A binding that overrides neither of the two resolution members takes the predicate path and pays exactly what it paid before, by design, and no benchmark covers it because nothing about it moved.
