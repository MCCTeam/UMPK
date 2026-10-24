# Owned server console forwarding

The UMPK server launcher now forwards every available stdin byte to its owned child. This repairs a harness prerequisite for vanilla hoe and shovel acceptance: a console command and its ordered `say` completion marker can arrive in one write without stranding the marker. This report proves launcher behavior; it does not establish native item-use behavior or turn the interrupted live attempts into valid red results.

## Defect and change

At base commit `3132e9943d7ed5d128453a8f0bca3ce93a2e0eeb`, `server.run()` selected the operating-system stdin descriptor and then called buffered `TextIOWrapper.readline()`. The first call could read two lines into Python while returning only one. The descriptor was then empty, so subsequent `select()` calls could not see the buffered second line. The focused reproduction sent `time set day\nsay ordered-completion-marker\n` in one pipe write while keeping the writer open; the child received only `time set day\n`.

The existing relay loop now uses `os.read()` and the child stdin binary buffer. It preserves byte order, partial lines, and split UTF-8 sequences without adding a timer or interpreting commands. On upstream EOF it closes child stdin once and continues the existing owned-process polling without repeatedly selecting an exhausted descriptor. Shutdown handles the closed pipe by terminating the owned child, while normal console `stop` and smoke shutdown retain their existing paths.

## Executed checks

Python `3.14.8` on Linux ran the production CLI and `run()` through actual subprocess pipes and loopback sockets. A tiny owned Python executable replaced Java, provided game-port/RCON readiness, journaled raw stdin bytes, and exposed EOF and shutdown. It supplied no Minecraft behavior. Each fixture used fresh directories and distinct temporary ports; no shared server, seed, launcher copy, or active run was modified.

| Check | Original launcher | Corrected launcher |
|---|---|---|
| Two queued commands while the writer remains open | Failed: second line absent | Passed: both lines received in order before EOF |
| Partial lines and split two-byte/four-byte UTF-8 characters | Failed: four fragments withheld until newline | Passed: each fragment received byte for byte |
| EOF after a final line without a newline | Failed: final bytes arrived but EOF did not | Passed: exact bytes and EOF received, child reaped |
| Wrapper termination after EOF with a child that remains alive | Failed: EOF did not reach the child | Passed: wrapper exited `130`, owned child received SIGTERM and exited `0` |
| Smoke readiness and console `stop` | Passed | Passed |

The focused red executed five tests: one passed and four failed for the recorded relay/EOF defects. The focused green executed all five and passed. The complete local official-tool and RCON suite then executed 19 tests and passed with no skips. Every green case checked `stopped.json`, the original child PID, zero child exit, an absent child process, and both closed ports. Temporary fixtures were removed. These add five deliberate Python regression tests; no .NET test baseline was changed.

The focused command was `python3 -m unittest -v test_console_forwarding`, run in `/tmp/umpk-console-forwarding-20261021-v1/source`; separate `red.log` and `green.log` captured exit codes `1` and `0`. The adjacent command was `python3 -m unittest discover -s /tmp/umpk-console-forwarding-20261021-v1/source -p 'test_*.py' -v`, run from the repository root and captured in `adjacent.log` with exit code `0`. The isolated directory contained byte-identical copies of the original launcher and `official.py`, then the reviewed launcher change and new tests. Full commands and counts are in the evidence receipt.

## Evidence and remaining checks

Raw evidence is retained outside Git at `/home/anon/Projects/MinecraftOfficial/runs/console-forwarding-focused-20261021-v1/`. Its 12-file `manifest.json` has SHA-256 `2758bc0bac230455890389bb0e53eed153c70c55e45d695840a02a3d284c03c3`. The original launcher hash is `7c9037c2c089e879e03e9aa684e075ab42b995ac796ced79d180e2632893c1fc`; the corrected launcher hash is `c2ee3c58d67daf34b8d92c142bae194d58c6b319262e69944150751e1604ab75`; the new test hash is `8ec08b0020029a7664991b4523a219ab74ff857c8723fa44560e6b0aee927808`.

DMCBK and MCC HEADs, complete Git status, and launcher hashes matched their before/after receipts. Their launcher copies deliberately retain the original hash because preserving sibling source takes precedence over synchronization guidance in this task. The change contains UMPK's launcher, its new test file, this scoped report and the CI regression step.

The existing Ubuntu build/test CI job now also runs `python3 -m unittest discover -s tools/minecraft-official -p 'test_*.py' -v`. This adds the launcher and RCON regression suite to an existing job; the workflow retains its seven jobs. That workflow change requires a check on the next published head.

Real vanilla hoe/shovel acceptance must be rerun with fresh launcher copies after this repair. The five earlier shovel console-marker failures remain harness failures. The full repository Python suite and final producer, consumer, package, AOT, and CI gates were not executed by this focused task.
