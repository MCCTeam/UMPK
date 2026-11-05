# General 26.3 movement diagnostic

The earlier General 26.3 run disconnected after the native server reported "Invalid move player packet received." Its internal second attempt passed, and the external driver correctly rejected that retry. The original failed batch and both native attempts remain retained. The cause is still unknown.

The test now records selected real movement frames before connection, copies their payloads within the observer callback, and writes bounded diagnostics after the owned client and server drain. It records position and angle bit patterns, finite-value flags, actual version-bound codec round trips, source and binary hashes, and attempt indices. Capture overflow or a diagnostic error fails the test. No production movement code, outgoing packet construction, retry rule, timeout, or existing feature assertion changes.

SnapshotSelfAsync adds awaited owner-loop barriers before the existing movement calls. Those barriers and the observer can change test scheduling. A passing instrumented run therefore does not prove timing equivalence or show that the earlier intermittent disconnect was fixed.

One fresh instrumented 26.3 leg passed on its first attempt using the actual Entry14 fixture and frozen d591/e19 producer. The 1,499,570-byte capture retained all 283 selected frames and 14 player-state snapshots without overflow or diagnostic errors. All bound-codec round trips matched the captured bytes. An independent offline check matched 14 outgoing movement payloads to their recorded numeric bit patterns and flags; all 3,134 checks of numeric bit patterns, finite-value flags and NaN flags passed. The native server stopped normally with peer disconnect and both stream EOFs observed; all seven recorded processes disappeared, the cgroup was removed, and both fixed ports closed.

The normal fixture run remains 76 rows: 49 passes and 27 exact named skips, with the SDK notExecuted counter at zero. This native diagnostic uses e19 and a private fixture composition. Older General rows retain their original source and binary provenance; current CA49 runtime evidence is separate.

Evidence: ACTUAL-GENERAL777-DIAGNOSTIC.json SHA256 086ec7aca7ced73e5994f2dc17a1d7c0cb08e2396f0a7f989cc4ea251c88dafb; canonical native acceptance SHA256 54aea53ad0cec3f907b2ba8e4afc1c13300f1b1adab2991701fe8dbfa607539b. Raw captures and logs remain outside Git.

The test retains the fixed 2,048-entry and 20,971,520-byte diagnostic caps. The six selected model families include every protocol-777 path knot and tick offset. Source publication copies the exact formatter-verified `449a2896` postimage from the executed Entry14/15 trial; it does not add another test case or change the existing 76-row normal count.
