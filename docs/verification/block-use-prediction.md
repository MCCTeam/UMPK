# Block-use prediction runtime

This report records the first typed block-use and activation slice over base `677ca1a5885ed3355abe332708cb51b1bc32084b`. Specialized placement and the complete item/block inventory remain open. Local prediction remains separate from server-observed `UseBlockOutcome`.

## Runtime decisions

`BlockUsePrediction` separates pre-send permission, native result (`Unknown`, `Pass`, `Fail`, `Consume`, `Success`) and swing (`None`, `Local`, `Packet`). The existing session-owned sender evaluates the prediction before committing a use packet. A predicted post-send failure still sends the packet; unknown context keeps packet-only behavior. A committed use/swing group finishes with its captured session cancellation token, and acknowledgment waits remain outside the owner loop. Protocol 777 keeps its local-only placement swing policy.

Hand validation runs before legacy fallback in both block use and air use. Protocol 47 rejects an offhand request instead of silently sending the main-hand stack; invalid hand values fail before sending. Supported offhand requests retain their own stack and packet hand.

Lever/button activation now runs before item cooldown and placement permissions. Secondary use suppresses activation when either hand holds an item. From protocol 766, the empty-hand activation fallback applies only to the main hand. Already-powered buttons consume without a swing from protocol 573, while protocol 498 retains its successful swing. The source audit covers all 44 mapped releases from 1.14.4 through 26.3 and their 32 protocol datasets; it checks every registered button family. Earlier activation families remain unknown in this slice.

## Evidence and commands

The external evidence root is `/tmp/umpk-placement-agent-20261019-v1`. `native-source-audit.json` has SHA-256 `e938c9db6763708c6bc1fe59615528eb73d8c7ee19b1a3c8ae105e28ceb1710a` and records original source/artifact hashes. `placement-slice-manifest.json`, SHA-256 `61a9e1e12233f6283209f723116a3ed6c04df9b4fb29b6ba1e986a6b15f5f629`, records exact source overlays, commands, log hashes and produced assembly hashes.

Tests use bound codecs at the packet sink and the production scheduled block-use caller, with known inventory, terrain and self bounds. They assert the emitted packet order/hand and unchanged authoritative world/inventory state. These component tests do not claim a real TCP server observation.

```bash
/home/anon/Projects/MinecraftOfficial/toolchains/dotnet-sdk-10.0.401/dotnet test /tmp/umpk-placement-agent-20261019-v1/source/tests/Umpk.Client.Tests/Umpk.Client.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~BlockUsePredictionRuntimeTests' --logger 'console;verbosity=normal'
```

The run uses the official SDK as `DOTNET_ROOT` and first on `PATH`, with both oracle-root variables set to `/home/anon/Projects/MinecraftOfficial`. Eight initial hand tests fail five cases before the guard and pass afterward. Lever tests then fail 144 of 232 cases; adding an unconnected result model retains those failures, and connecting the caller passes 232. The button expansion fails 53 of 360 cases before its source-backed boundary and passes afterward. Air-use validation adds eight cases, five of which fail before its guard. The final focused run passes all 368 cases. The adjacent placement/collision/cursor/use-block selection passes 1,756 tests, with no failures or skips.

Both scoped format checks exit zero and change no source files. The earlier formatter attempt failed because its child build host selected the system runtime; correcting the SDK environment resolved that harness failure. The combined frozen mining/placement overlay also passes 2,186 focused/adjacent cases, recorded in `/tmp/umpk-root-picker-20261019-v1/integration-results.json` and its source manifest. Full immutable-producer gates, consumer/AOT checks and live acceptance of this new slice remain pending. A separate baseline 1.9 live run observed accepted placement without the expected swing; attribution remains open and is not counted as a pass.

## Bounded native hoe use

The [hoe runtime report](hoe-use.md) records a later source-backed tool-on-block slice for protocols 498–776. The existing block-use owner now predicts ordinary soil/path and rooted-dirt results, preserves swings with worn stacks, and retains unknowns for older/new data-driven eras and unclassified components. All 642 new runtime cases and three matched live boundary legs pass; full immutable producer gates remain pending. Other specialized placement, tool and fluid families remain open.
