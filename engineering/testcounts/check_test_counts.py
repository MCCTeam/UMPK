#!/usr/bin/env python3
"""
D7 detection: compare actual per-suite dotnet-test counts against an expected
baseline and fail LOUDLY on any mismatch, any missing suite, or any sign that a
test host aborted/crashed - even when every individual per-project summary line
says "Passed!" with Failed: 0.

Why this exists: `dotnet test` on a multi-project .sln prints one independent
"Passed!"/"Failed!" line per project, with NO overall aggregate. A test host that
crashes, is killed for a hang, or otherwise dies mid-assembly still gets its own
line printed by the aggregator using only the results it received before the host
died - so that line reads "Passed!  - Failed: 0, Passed: N, ..." with N silently
smaller than the real suite size. The per-project exit code for a crashed project
IS non-zero, and `dotnet test` at the .sln level DOES propagate that as the overall
process exit code - but nothing stops a human (or a script) from looking at one
assembly's line in isolation and being satisfied by "Passed!". This was proven by
direct experiment during the D7 investigation (see PROGRESS-d7.md / FEEDBACK-d7.md
in the umpk repo): a test host killed via Environment.Exit, Environment.FailFast,
or an unrecoverable native stack overflow all produced the identical, misleading
shape - console line green, Failed: 0, Total reduced, "Test Run Aborted." printed
elsewhere in the log, exit code 1.

Usage:
    dotnet test --no-build -c Release 2>&1 | tee test-output.log
    python3 check_test_counts.py test-output.log --baseline expected_counts.json

    # or read stdin directly:
    dotnet test --no-build -c Release 2>&1 | python3 check_test_counts.py - --baseline expected_counts.json

Exit code 0 only if every expected suite appeared with the expected total AND no
abort/crash text was seen anywhere in the log. Exit code 1 otherwise, with a loud,
specific report of what's wrong - never silent.

To refresh the baseline after deliberately adding/removing tests:
    python3 check_test_counts.py test-output.log --write-baseline expected_counts.json
"""
import argparse
import json
import re
import sys

SUMMARY_RE = re.compile(
    r"^(Passed|Failed)!\s+-\s+Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+),"
    r"\s*Duration:\s*([^-]+?)\s*-\s*(\S+\.dll)",
    re.MULTILINE,
)

ABORT_MARKERS = (
    "Test Run Aborted",
    "Test host process crashed",
    "host process exited unexpectedly",
    "Test run was aborted",
)


def parse(text):
    suites = {}
    for m in SUMMARY_RE.finditer(text):
        status, failed, passed, skipped, total, duration, dll = m.groups()
        suites[dll] = {
            "status": status,
            "failed": int(failed),
            "passed": int(passed),
            "skipped": int(skipped),
            "total": int(total),
        }
    abort_hits = [marker for marker in ABORT_MARKERS if marker in text]
    return suites, abort_hits


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("logfile", help="path to captured dotnet test output, or '-' for stdin")
    ap.add_argument("--baseline", help="path to expected-counts JSON ({dll: total})")
    ap.add_argument("--write-baseline", help="write observed totals as the new baseline and exit")
    args = ap.parse_args()

    text = sys.stdin.read() if args.logfile == "-" else open(args.logfile, encoding="utf-8", errors="replace").read()
    suites, abort_hits = parse(text)

    if args.write_baseline:
        baseline = {dll: info["total"] for dll, info in suites.items()}
        with open(args.write_baseline, "w") as f:
            json.dump(baseline, f, indent=2, sort_keys=True)
        print(f"Wrote baseline for {len(baseline)} suites to {args.write_baseline}")
        return 0

    if not args.baseline:
        print("ERROR: --baseline is required unless --write-baseline is used.", file=sys.stderr)
        return 2

    with open(args.baseline) as f:
        expected = json.load(f)

    problems = []

    for dll, exp_total in expected.items():
        if dll not in suites:
            problems.append(f"MISSING SUITE: {dll} never reported a result at all (expected total={exp_total}).")
            continue
        got = suites[dll]
        if got["total"] != exp_total:
            sign = "-" if got["total"] < exp_total else "+"
            problems.append(
                f"COUNT MISMATCH: {dll} reported Total={got['total']} but baseline expects {exp_total} "
                f"({sign}{abs(got['total'] - exp_total)}). Status line said '{got['status']}!' "
                f"with Failed={got['failed']} - a green line is NOT proof this suite is intact."
            )
        if got["failed"] > 0:
            problems.append(f"FAILURES: {dll} reported Failed={got['failed']} (already visible, but noting it here too).")

    unexpected = [dll for dll in suites if dll not in expected]
    for dll in unexpected:
        problems.append(f"UNEXPECTED SUITE: {dll} reported results but is not in the baseline (new project? update baseline).")

    if abort_hits:
        problems.append(
            "ABORT TEXT SEEN IN OUTPUT: " + "; ".join(abort_hits) + " - at least one test host did not "
            "finish normally. Check which suite's line appears directly above/below this text; its count "
            "is not trustworthy even if it says 'Passed!'."
        )

    if problems:
        print("=" * 78)
        print("D7 DETECTION: test run counts do NOT match the expected baseline.")
        print("A green 'Passed!' line per suite is not proof the suite ran to completion.")
        print("=" * 78)
        for p in problems:
            print(f"  - {p}")
        print()
        print(f"Suites checked: {len(expected)}, suites reported: {len(suites)}, problems: {len(problems)}")
        return 1

    print(f"OK: all {len(expected)} expected suites reported their expected totals, no abort/crash text seen.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
