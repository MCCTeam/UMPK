---
title: "Development"
description: "The build gate you must pass before calling a change done, the repository layout, and the conventions that fail a build when broken."
sidebar:
  order: 1
---

UMPK has a strict build. Warnings are errors, the public surface is tracked in checked-in files, and several ordinary .NET APIs are banned outright. None of that is fussiness for its own sake, and the reasoning is worth reading before you fight it. But the practical part comes first.

## Prerequisites

- Install the .NET 10 SDK.
- Install Python 3.10 or later. The count detector and the extraction tooling need Python.

## The build gate

Run all five steps, in this order, before you call a change done. Run every command from the repository root.

1. Build the solution.

   ```bash
   dotnet build UMPK.sln
   ```

The build must report 0 warnings and 0 errors. Warnings are errors in this repository.

2. Run the full test suite and capture the log.

   ```bash
   dotnet test UMPK.sln 2>&1 | tee /tmp/test.log
   ```

The count detector must find 12,407 tests across 17 suites. It detects missing suites, total-count changes, failed tests, and aborted test hosts.

3. A green test run is not proof on its own. Run the count detector over the log.

   ```bash
   python3 engineering/testcounts/check_test_counts.py /tmp/test.log --baseline engineering/testcounts/expected_counts.json
   ```

The detector must report every suite at its expected total. [Testing](testing.md) explains what the detector catches.

4. Check the formatting.

   ```bash
   dotnet format --verify-no-changes
   ```

The command must exit with code 0.

5. Verify the dataset.

   ```bash
   dotnet run --project tools/Umpk.DataGen -- verify --data data/java
   ```

The command must print `verify: OK (50 protocols)`.

If you changed anything under `data/java/`, you must also regenerate the data package and prove the result is unchanged. See [the dataset](../concepts/the-dataset.md) for that procedure.

Continuous integration runs the build and test suite on Linux, Windows and macOS. Separate Linux jobs run the format check, dataset verify, generated-code freshness checks for `Umpk.Data.Java` and `Umpk.Protocol.Java`, and the legacy extraction verifier. CI does not run the count detector, so run the detector locally. Generate and compare `Umpk.Data.Lang` locally when language data or its emitter changes.

## Working on one suite

Do not run the full solution suite on every edit. It is slow.

1. Run one suite.

   ```bash
   dotnet test tests/Umpk.Physics.Tests
   ```

2. Run one class or one test.

   ```bash
   dotnet test tests/Umpk.Protocol.Java.Tests --filter "FullyQualifiedName~AscendTemplate"
   ```

3. Run the full gate before you finish. A filtered run proves nothing about the suites you skipped.

## Repository layout

| Directory | Holds |
| --- | --- |
| `src/` | 16 projects: 13 with code, 2 reserved and empty, and the `Umpk` meta package |
| `data/java/` | the canonical dataset, one directory per protocol |
| `tools/` | `Umpk.DataGen`, `Umpk.PacketRecorder`, and the Python extraction drivers |
| `tests/` | unit, conformance and integration suites, plus the shared TestKit and benchmarks |
| `fixtures/` | recorded packet captures and the pin files |
| `samples/` | `StatusPing` and `MinimalBot` |
| `engineering/` | build policies, validation baselines, and repository maintenance tools |
| `docs/` | this documentation site |

[Packages](../packages/overview.md) covers what each package owns. Two names in `src/` are reserved and empty: `Umpk.Server` and `Umpk.Proxy` contain no code at all. See [limitations](../reference/limitations.md).

Read [Using AI](using-ai.md) before you use an AI tool on this repository. The same review, evidence, and validation rules apply to assisted changes.

## Conventions that fail the build

Four rules are enforced by the compiler or by an analyzer, not by review. Each one fails the build when you break it, which is the intent.

### Public API tracking

Every public declaration must appear in its package's `PublicAPI.Unshipped.txt`. The `Microsoft.CodeAnalysis.PublicApiAnalyzers` package reports a missing entry as `RS0016`, and `RS0016` is an error here.

To add a public member:

1. Add the member.
2. Build the package.
3. Copy the exact declaration string from the `RS0016` message into `PublicAPI.Unshipped.txt`.
4. Rebuild the package.

Nothing has shipped yet, so `PublicAPI.Shipped.txt` is empty in every package. The practical effect is that no name is frozen and every public change is visible as a diff on a text file. Removals from the unshipped surface are allowed. State a removal in the commit message.

### Banned symbols

`engineering/BannedSymbols.txt` is short and every line has a reason attached:

```text
T:System.Console;Library code never touches the console; report through ILogger events
T:System.Activator;No runtime activation; the design has a generated-table or explicit-registration answer
M:System.Reflection.Assembly.GetTypes;No assembly scanning in src/
M:System.Reflection.Assembly.GetExportedTypes;No assembly scanning in src/
```

`System.Console` is banned because a package that prints to standard output takes a decision away from the application hosting it. Report through `ILogger` instead, or raise an event.

The other three are banned for Native AOT. Reflection-based activation and assembly scanning defeat trimming, and a trimmer that cannot prove a type is unused keeps it. Every package sets `IsAotCompatible`, so the design always needs a generated-table or explicit-registration answer instead of a scan. The registration table in [the packet pipeline](../concepts/packet-pipeline.md) is what that constraint produces.

### Nullable reference types

`Nullable` is enabled for the whole repository, and a nullability warning is an error. Prefer the `Try*` pattern for expected failures. Use `ArgumentNullException.ThrowIfNull` at public entry points.

### Generated files

Do not edit a file whose name ends in `.g.cs`. The repository has 59 generated C# files: 53 in `Umpk.Data.Java`, two in `Umpk.Data.Lang`, and four in `Umpk.Protocol.Java`. Change the dataset or the emitter, then regenerate.

## Test counts are part of the change

The count baseline in `engineering/testcounts/expected_counts.json` is a contract, not a cache.

1. Add or remove tests.
2. Run the full suite.
3. Edit the affected suite total in `engineering/testcounts/expected_counts.json`.
4. State the old total and the new total in the commit message.

## The parts that are not mechanical

Everything above is checkable by a machine, which is why it is written as steps. The review bar that actually decides whether a change is good is not.

Verify behavior against vanilla and retain the detailed research with the change review. Source comments should describe the resulting behavior or invariant, not raw source locations. See [vanilla as the oracle](../concepts/vanilla-as-the-oracle.md). Prove codec work with bytes rather than shapes, because a successful decode may still have interpreted the frame incorrectly. Write the failing test first and confirm that it fails for the expected reason.

And be honest about what your evidence supports. "Twenty clean runs bound the failure rate below something" is a real statement. "This is fixed" usually is not.
