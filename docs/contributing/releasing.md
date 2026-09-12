---
title: Releasing NuGet packages
description: One-time NuGet.org and GitHub setup, the release checklist, and recovery rules for publishing all UMPK packages.
sidebar:
  order: 5
---

UMPK publishes fourteen packages together from a `v<version>` Git tag. A release workflow rebuilds and verifies the repository, creates primary and symbol packages, checks their metadata and contents, publishes them to NuGet.org with a short-lived OpenID Connect credential, and then creates a GitHub Release containing the packages and SHA-256 checksums. `Umpk.Server` and `Umpk.Proxy` are reserved projects and are never packed.

## One-time account setup

Complete these steps before pushing the first release tag.

1. Create or select the NuGet.org account or organization that will own every `Umpk*` package ID. The same owner must cover all fourteen packages.
2. In the GitHub repository, create an environment named `nuget`. Add required reviewers so a tag cannot publish until a maintainer approves the deployment. Restrict deployment branches and tags to release tags if the repository settings allow it.
3. Add an environment secret named `NUGET_USER`. Set it to the NuGet.org profile name that owns the trusted-publishing policy, not an email address or API key.
4. On NuGet.org, open the account's Trusted Publishing settings and add a GitHub Actions policy with repository owner `MCCTeam`, repository `UMPK`, workflow file `release.yml`, and environment `nuget`. Scope the policy to the UMPK packages owned by that account. NuGet.org may show a new policy as pending until its first successful publish.
5. In GitHub branch protection or rulesets, require the PR workflow's build, test, format, dataset, generated-output, AOT, and NuGet package jobs before merging to `master`.

The release workflow requests `id-token: write` only in the NuGet publishing job. `NuGet/login` exchanges that GitHub OIDC identity for a temporary NuGet key immediately before upload, so the repository does not store a long-lived publishing key.

## Prepare a release

1. Update the single `<Version>` value in `Directory.Build.props`. Use SemVer without the leading `v`, for example `0.9.1` or `1.0.0-rc.1`.
2. Add the same version and release date to `CHANGELOG.md`. Describe consumer-visible changes and migration requirements.
3. Run the local release dry run from the repository root:

   ```bash
   dotnet restore UMPK.sln
   dotnet build UMPK.sln --no-restore -c Release
   dotnet test UMPK.sln --no-build -c Release 2>&1 | tee /tmp/umpk-test.log
   python3 engineering/testcounts/check_test_counts.py /tmp/umpk-test.log --baseline engineering/testcounts/expected_counts.json
   dotnet format UMPK.sln --verify-no-changes --no-restore
   dotnet run --project tools/Umpk.DataGen --no-build -c Release -- verify --data data/java
   python3 tools/extraction/extract_lang.py --data data/java --check
   dotnet run --project tools/Umpk.DataGen --no-build -c Release -- generate --data data/java --out src/Umpk.Data.Java --out-lang src/Umpk.Data.Lang --protocol-out src/Umpk.Protocol.Java
   git diff --exit-code data/java src/Umpk.Data.Java src/Umpk.Data.Lang src/Umpk.Protocol.Java
   dotnet pack UMPK.sln --no-build -c Release -o artifacts/packages
   version=$(dotnet msbuild src/Umpk/Umpk.csproj -nologo -getProperty:Version)
   python3 engineering/release/verify_packages.py --packages artifacts/packages --version "$version"
   ```

   Keep the complete test output in `/tmp`; do not add it to the repository. The language extraction check requires the ignored representative Mojang server jars under `MinecraftOfficial/downloads/`; it is intentionally a maintainer-side release prerequisite because those artifacts cannot be committed or provisioned from this repository. CI still verifies every committed language dataset and regenerates `Umpk.Data.Lang` from it.
4. Open a release-preparation pull request and wait for every required check to pass. Review every `.nupkg` metadata change, generated diff, and fixture diff. Merge the exact commit that should be released.

## Publish

Tagging and pushing are maintainer actions. Confirm the target commit and version before running them:

```bash
git switch master
git pull --ff-only
version=$(dotnet msbuild src/Umpk/Umpk.csproj -nologo -getProperty:Version)
git tag -a "v$version" -m "UMPK $version"
git push origin "v$version"
```

The workflow requires an annotated tag on a commit contained in `master`, and the tag must match `Directory.Build.props` exactly or it stops before packing. Approve the protected `nuget` deployment when GitHub prompts. After the workflow finishes, verify all fourteen primary packages and thirteen symbol packages on NuGet.org, then verify the GitHub Release contains the same files and `SHA256SUMS`.

## Failure and recovery

NuGet package versions are immutable and publishing fourteen IDs is not transactional. The workflow uses `--skip-duplicate` so rerunning a partially completed job safely skips packages already accepted by NuGet.org and continues with missing packages. Never move or recreate a published tag and never reuse a version for different bits.

If a bad package is already public, unlist the affected version on NuGet.org, fix the repository, increment the patch version, and publish a new tag. Do not delete and replace release artifacts under the old version. If NuGet publishing fails before any package is accepted, fix the account policy or workflow configuration and rerun the failed job on the unchanged tag.
