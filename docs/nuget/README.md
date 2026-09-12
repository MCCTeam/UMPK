# UMPK

UMPK is a .NET 10 toolkit for programs that speak the Minecraft Java Edition protocol. One codebase supports protocol 47 (Minecraft 1.8) through protocol 776 (Minecraft 26.2), with generated version data, a supervised client runtime, authentication, game state, chat, inventory, physics, and pathfinding.

Install the complete client stack:

```shell
dotnet add package Umpk
```

You can instead install a focused package such as `Umpk.Protocol.Java`, `Umpk.Nbt`, or `Umpk.Text`. See the [package overview](https://github.com/MCCTeam/UMPK/blob/main/docs/packages/overview.md) for the full dependency map.

UMPK is pre-1.0 software. Pin the package version that your application uses because public APIs can change between releases.

- [Documentation](https://github.com/MCCTeam/UMPK/tree/main/docs)
- [Getting started](https://github.com/MCCTeam/UMPK/blob/main/docs/getting-started/installation.md)
- [Supported Minecraft versions](https://github.com/MCCTeam/UMPK/blob/main/docs/reference/supported-versions.md)
- [Changelog](https://github.com/MCCTeam/UMPK/blob/main/CHANGELOG.md)
- [Source and issues](https://github.com/MCCTeam/UMPK)
