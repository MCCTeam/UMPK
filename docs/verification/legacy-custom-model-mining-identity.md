# Legacy custom model integer identity in held mining

## Behavior

Protocols 766–768 compare the native custom model integer when they validate a held mining target. Equal integers preserve progress; different integers restart mining even when their shared public float projection is identical. The [integer codec proof](legacy-custom-model-data.md) establishes the original values, packets, DATA hashes and explicit-zero-versus-absence distinction. Modern four-list behavior on 769–777 stays unchanged.

The existing `VanillaMiningItemIdentity` owner checks known legacy integers before generic effective-map equality. It preserves received integer provenance and conservative small integral values, including record copies that share the original list. A changed or copied ambiguous projection remains unknown. The existing nullable caller sends one abort and ends that request without inventing a restart; its diagnostic now describes component identity rather than Lore alone. Public actions, models, APIs, datasets, dependencies and DI lifetimes are unchanged.

## Connected proof

The 54 cases cover 1.20.5/766, 1.21/767 and 1.21.2/768. Thirty-nine deliver the original native full set-slot packet pairs through bound codecs and a connected session, asserting decoded slot/item/count, observed float bits, byte-identical round trips and public `DigBlockAsync` packet cadence. The original integer DATA hash assertions remain executed in the separately passed Protocol 54 tests. Fifteen explicitly local model controls cover record copies, a known small edit, an ambiguous edit, copied-list provenance loss and equal record wrappers with unknown values. They run on the session owner and are distinct from remote-frame proof.

Baseline Client `c35755f7` passes 18 controls and fails 36 causal cases. The identity-only owner passes 45 cases; nine unknown paths stop correctly and fail only the old diagnostic wording. The generic caller message then passes the unchanged 54. All 611 adjacent cases pass, including modern custom model 108 and Lore 65 with all six primitive-unknown assertions. One unfiltered run passes all 37,358 Client tests with zero skips in 308.772 seconds. Its exact multiset preserves 37,304 old cases and adds 54, permitting only three prebound corpus-root display mappings for 127 occurrences after proving all 242 capture/sidecar files and both enumerator sources identical.

`ACTUAL-CLIENT54.json`, SHA256 `8f946af0d4173bacfc12e8098ecf088d9aa5f8ab49d2c0c5729201a6ede2d407`, under `.scratch/legacy-custom-model-identity-preparation-20261104-v1` indexes exact commands, source/input manifests, TRXs and cleanup. The external job is `/home/anon/Projects/MinecraftOfficial/runs/legacy-custom-model-client54-20261104-v2`. It binds admitted `2a66bef` plus tested Protocol `83d64ac9`; no Protocol compiler or server runs in this capsule.

## Producer and cleanup

Normal formatting changes zero bytes. The three actual Csc processes are one Test compile and two staged Client compiles, each with processor count one. Candidate Client DLL is `1eed2ecc481dff943f7a9e0ad44392d70a66eef32bc6b78eea0293557ffd8aef`, PDB `bf730d0755b7b8df6a118dd4f9e4ddb6fd23132a8a18c7e59afacf72befa8af3`, XML `9e4543058220f25c40adb6092a3a47acf66501c886c5cd17bcf7ad80576a2f6f`. All 226 authored Client source hashes occur in its 229-document PDB. All 4,433 original source files match expected bytes, all 39 producer inputs and 20 frozen references remain unchanged, and only Client DLL/PDB change in the 136-file runtime. XML and the tested Test DLL/PDB remain identical.

Peak memory is 3,736,989,696 bytes under the unchanged 4 GiB/200% CPU/no-swap/no-core-dump cap, with zero limit/OOM events. Cleanup SHA256 `aa9caf405d996554da3808e1c695630743c3ac58b9d096a44f3f9216ce58dd18` verifies all 30 observed HOST PIDs absent, the exact successful unit inactive with MainPID zero and its cgroup removed. No workload remains. The deliberate count change is Client 37,304→37,358 and seventeen-suite expected total 51,976→52,030; Integration remains 71.

## Retained failure and remaining gates

The first Client Test compile stopped with CS0122 because its fixture directly called Protocol-internal hash helpers. Production Client has IVT access; Client.Tests does not. The original independent source review missed that distinction and is explicitly superseded. The corrected fixture keeps every native frame, round trip and mining assertion through public types; all integer DATA hash checks remain in the passed Protocol tests. The failed source, compiler evidence, superseded review, corrected assembly-access review and eleven-process cleanup remain separate from valid runtime RED/GREEN. No IVT or public API is widened.

Final combined producer, producer-matched DMCBK/MCC, strict-size Native AOT, package, live matrix and actual published-head CI gates remain required. Other structured component families and unmodeled text stay explicit unfinished behavior. This bounded comparison provides no generic component equality.
