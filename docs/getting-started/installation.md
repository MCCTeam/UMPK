---
title: "Installation"
description: "How to install UMPK from NuGet or reference a checkout, and which packages a client or status ping needs."
sidebar:
  order: 1
---

## What you need

.NET 10. Every project in `src/` inherits `TargetFramework` `net10.0` from the repository's `Directory.Build.props`, along with `Nullable` enable, `ImplicitUsings` enable and `TreatWarningsAsErrors` true. There is no multi-targeting and no older TFM.

Nothing else is required to build. The library's runtime dependencies are deliberately small: `Microsoft.Extensions.Logging.Abstractions` in `Umpk.Core`, `Umpk.Auth` and `Umpk.Realms`, `Microsoft.Extensions.Options` and `Microsoft.Extensions.DependencyInjection.Abstractions` in `Umpk.Client`, and `Brigadier.NET` in `Umpk.Commands`. Everything else is project references between the packages themselves.

## Install from NuGet

Install the `Umpk` meta package when you want the complete client stack:

```bash
dotnet add package Umpk --version 0.9.0-beta.1
```

Install a focused package when you only need one layer. For example, a status tool can start with the Java protocol package, while an NBT utility needs only the NBT package:

```bash
dotnet add package Umpk.Protocol.Java --version 0.9.0-beta.1
dotnet add package Umpk.Nbt --version 0.9.0-beta.1
```

All packages in one release use the same version. The project is pre-1.0, so pin that version rather than using a floating range.

## Reference a checkout

Use project references when you want to test an unreleased commit or contribute to UMPK. The samples use this arrangement and are the canonical local examples.

To get a working checkout:

1. Clone the repository.

   ```bash
   git clone https://github.com/MCCTeam/UMPK.git
   ```

2. Change to the repository root.

   ```bash
   cd UMPK
   ```

3. Build the solution. The build must report zero warnings and zero errors, because warnings are errors in this repository.

   ```bash
   dotnet build UMPK.sln
   ```

## What to reference

There is a meta package, `Umpk`, whose entire content is six project references: `Umpk.Client`, `Umpk.Data.Java`, `Umpk.Data.Lang`, `Umpk.Auth`, `Umpk.Physics`, `Umpk.Pathfinding`. Referencing it pulls in everything those drag along, which is most of the library. It contains no code of its own.

Otherwise, reference what you actually use. The two samples are the worked answer.

To reference a checkout for a status ping, add these three project references to your `.csproj` file. A status ping needs no client package.

```xml
<ItemGroup>
  <!-- ServerEndpoint -->
  <ProjectReference Include="../../src/Umpk.Core/Umpk.Core.csproj" />
  <!-- JavaStatus, JavaLegacyPing, the TCP factory and the SRV resolver -->
  <ProjectReference Include="../../src/Umpk.Protocol.Java/Umpk.Protocol.Java.csproj" />
  <!-- Reading the MOTD component out of the status JSON -->
  <ProjectReference Include="../../src/Umpk.Text/Umpk.Text.csproj" />
</ItemGroup>
```

To reference a checkout for a client that joins a server, add these five project references to your `.csproj` file. The `Umpk.Auth` package supplies the offline identity.

```xml
<ItemGroup>
  <ProjectReference Include="../../src/Umpk.Core/Umpk.Core.csproj" />
  <ProjectReference Include="../../src/Umpk.Client/Umpk.Client.csproj" />
  <ProjectReference Include="../../src/Umpk.Protocol.Java/Umpk.Protocol.Java.csproj" />
  <ProjectReference Include="../../src/Umpk.Data.Java/Umpk.Data.Java.csproj" />
  <ProjectReference Include="../../src/Umpk.Auth/Umpk.Auth.csproj" />
</ItemGroup>
```

`Umpk.Client` already references `Umpk.Game`, `Umpk.Text`, `Umpk.Commands`, `Umpk.Physics`, `Umpk.Pathfinding` and `Umpk.Data.Java` transitively, so those come along whether or not you name them. Naming the ones you use directly is still worth doing, since it keeps the intent visible.

Two project names in `src/` are reserved and empty. `Umpk.Server` and `Umpk.Proxy` contain no `.cs` files and are not published. See [Packages](../packages/overview.md) for the full map.

## AOT

`src/Directory.Build.props` sets `IsAotCompatible` true for every shipping project, which turns on the trim and AOT analyzers across the whole surface. That is also why reflection is not merely discouraged here: `System.Activator`, `Assembly.GetTypes` and `Assembly.GetExportedTypes` are banned symbols that fail the build. Per-version behavior comes from generated tables rather than from runtime type discovery.

Samples opt out by default (`samples/Directory.Build.props` sets `IsAotCompatible` false), but `samples/MinimalBot` opts back in and sets `PublishAot` true. CI publishes that sample for Linux, checks its executable against the size budget, and runs the AOT integration smoke against it.

## The public API is not frozen

Every declaration in every package sits in `PublicAPI.Unshipped.txt`. All sixteen `PublicAPI.Shipped.txt` files hold nothing but the `#nullable enable` header. Practically:

- Any public name can be renamed or removed before 1.0, and removals from an unshipped surface are allowed by the project's own rules as long as the commit says so.
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` runs as an error, not a warning, so an API change that is not recorded fails the build. That gives you a reviewable diff of every surface change, which is the closest thing to a stability guarantee on offer right now.

Pin a package version for released builds or a commit for checkout builds.

## Checking your setup

To confirm the reference chain:

1. Build the solution. The build must report zero warnings and zero errors.

   ```bash
   dotnet build UMPK.sln
   ```

2. Run the status ping sample against a server you can reach. Replace `mc.example.com` with the address of that server.

   ```bash
   dotnet run --project samples/StatusPing -- mc.example.com
   ```

3. Read the output. The output must contain a version name and a player count.

The walkthrough of that sample is in [your first status ping](status-ping.md).
