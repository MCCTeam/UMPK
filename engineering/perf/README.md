# engineering/perf

Performance baselines for the inbound frame path, and the one performance number the build enforces.

## What is here

| File | What it is |
| --- | --- |
| `baselines-<date>.md` | A dated BenchmarkDotNet run: the machine, the job settings, every table, and what the numbers say. Baselines are recorded before code moves, not after. |
| `frame-dispatch-resolve-once.md` | A re-measurement of one benchmark against a baseline: same machine, same job, before and after side by side. |
| `allocation_budgets.json` | Bytes allocated per decode, per corpus-covered packet family, with the frame each was measured on. `AllocationBudgetTests` enforces it. |

## Running the benchmarks

```bash
dotnet run -c Release --project tests/Umpk.Benchmarks -- \
  --filter "Umpk.Benchmarks.FrameDispatchBenchmark*" \
           "Umpk.Benchmarks.FrameCopyBenchmark*" \
           "Umpk.Benchmarks.ChunkDecodeBenchmark*" \
           "Umpk.Benchmarks.ItemStackDecodeBenchmark*" \
           "Umpk.Benchmarks.VarIntBenchmark*" \
           "Umpk.Benchmarks.DescriptorBuildBenchmark*"
```

They do not run in CI: too slow, and far too noisy on a shared runner for the sub-100 ns operations most of them measure. They run on demand and their results are committed here.

## The acceptance rule

**Allocation is a gate.** No packet family may allocate more per frame than `allocation_budgets.json` says. That is deterministic, runs in milliseconds and catches the regression worth catching: a copy reintroduced into a decode path. A budget is the measured number plus the greater of 8 bytes and 5%, and it rises only in a commit whose message names the field or table that grew and carries the matching `[MemoryDiagnoser]` number.

Re-pin after a deliberate model change:

```bash
UMPK_UPDATE_ALLOCATION_BUDGETS=1 dotnet test tests/Umpk.Protocol.Java.Tests \
  --filter "FullyQualifiedName~PinnedBudgets_AreExactlyTheCorpusCoveredFamilies"
```

**Time is not a gate.** BenchmarkDotNet runs on an unpinned workstation, and any threshold tight enough to be interesting sits below the run-to-run noise floor. The rule is a procedure instead: pin the run conditions (the same `[SimpleJob]` settings, the same machine, no other load), report the confidence interval the tool prints, and treat a change as real only when the intervals do not overlap. The verdict is a human's.

## Coverage, stated honestly

`allocation_budgets.json` reaches only what the corpus holds. The header records both numbers at pin time: distinct implemented bindings across the catalog, and how many of them a recorded frame reaches. Families outside that set are not measured, and the file says so rather than implying coverage it does not have. Every capture that lands widens the gate, because the pinned rows must be exactly the corpus-covered families or the suite fails.
