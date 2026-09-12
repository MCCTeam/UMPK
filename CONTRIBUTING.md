# Contributing to UMPK

Thanks for helping with UMPK. Small, well-tested changes are easier to review and safer across 49 protocol revisions.

Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before you start

1. Search existing issues and pull requests.
2. Open an issue before a large API, architecture, dependency, or protocol-data change.
3. State which Minecraft versions and protocol numbers the change affects.
4. Use a server that you control for live tests.

You need the .NET 10 SDK. Protocol extraction also needs Python 3.10 or later and a compatible Java runtime. Keep Mojang jars, mappings, and decompiled source in the ignored `MinecraftOfficial/` directory.

## Build the repository

```bash
dotnet restore
dotnet build UMPK.sln
dotnet test UMPK.sln
```

Run one project or test while you work:

```bash
dotnet test tests/Umpk.Client.Tests
dotnet test tests/Umpk.Protocol.Java.Tests --filter "FullyQualifiedName~PacketName"
```

## Make a change

1. Reproduce the problem with a focused test.
2. Check version-specific behavior against the matching vanilla release.
3. Make the smallest change that fixes the problem.
4. Run the focused test and nearby version boundaries.
5. Update public API files, generated data, documentation, and test counts when required.
6. Run the complete validation gate.

Do not add server-specific workarounds. Put era differences in the dataset or existing capability model. Do not hardcode protocol numbers in shared engine code.

## Generated files and protocol data

Do not edit `.g.cs` files or generated pins by hand. Change the source dataset or generator, then regenerate the output.

Read [Adding a version](docs/contributing/adding-a-version.md) before a protocol change. Record the official artifact, version, hash, source location, and observed wire behavior in the pull request.

Never commit Minecraft jars, libraries, decompiled source, credentials, token caches, raw logs, heap dumps, or private packet captures.

## Validation gate

Run these commands from the repository root:

```bash
dotnet build UMPK.sln
dotnet test UMPK.sln 2>&1 | tee /tmp/umpk-test.log
python3 engineering/testcounts/check_test_counts.py /tmp/umpk-test.log \
  --baseline engineering/testcounts/expected_counts.json
dotnet format --verify-no-changes
dotnet run --project tools/Umpk.DataGen -- verify --data data/java
python3 tools/extraction/extract_lang.py --data data/java --check
```

If generated data changed, regenerate it and check the diff:

```bash
dotnet run --project tools/Umpk.DataGen -- generate \
  --data data/java \
  --out src/Umpk.Data.Java \
  --out-lang src/Umpk.Data.Lang \
  --protocol-out src/Umpk.Protocol.Java
git diff --exit-code -- src/Umpk.Data.Java src/Umpk.Data.Lang src/Umpk.Protocol.Java
```

A skipped test or a missing local server is not a pass. State every check that you could not run. Store temporary logs outside the repository.

## Pull requests

Keep each pull request focused. Explain the problem, the chosen behavior, affected versions, and validation results. Include screenshots only when they help review output or documentation.

Review your own diff before submission. Remove debugging code and unrelated formatting. Check that no secret or downloaded game artifact entered the commit.

Maintainers preparing a package release must follow [Releasing NuGet packages](docs/contributing/releasing.md), including the one-time trusted-publishing setup and the local artifact-backed language check.

## Using AI tools

AI output is an untrusted draft. Review every changed line and check all protocol claims against official artifacts or captured bytes. Do not paste secrets, user data, auth caches, private server addresses, or private captures into an AI service.

Read [Using AI](docs/contributing/using-ai.md) for the full policy.
