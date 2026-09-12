## What changed?

Describe the problem and the change. Keep this focused on behavior that a reviewer can check.

## Versions affected

List the Minecraft release names and protocol numbers. Write "all supported versions" only when you tested or proved that scope.

## Evidence

Link the issue and include any vanilla source, artifact hash, packet capture, or failure reproduction that supports the change.

## Validation

List the exact commands and results. State any check that you could not run.

- [ ] Focused tests pass.
- [ ] `dotnet build UMPK.sln` passes with no warnings.
- [ ] The full test count matches `engineering/testcounts/expected_counts.json`.
- [ ] `dotnet format --verify-no-changes` passes.
- [ ] Data and generated files are current when applicable.
- [ ] Public API files are current when applicable.
- [ ] The change contains no secrets, raw logs, Minecraft artifacts, or unrelated changes.

## Notes for reviewers

Call out compatibility risks, public API changes, generated diffs, or follow-up work.
