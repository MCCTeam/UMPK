// VersionTable lists every protocol UMPK speaks, with no server involved. It prints the full catalog, then shows the two lookups a bot uses at startup.
//
// Run it like this:
//
//   dotnet run --project samples/VersionTable
//   dotnet run --project samples/VersionTable -- 772
//   dotnet run --project samples/VersionTable -- 1.21.8
//
// A bot needs this because a client is built for one protocol. The usual flow is ping first, read version.protocol, then build the client for that version. MinimalBot does exactly this. This sample shows the catalog side of it.
using Umpk.Data.Java;
using Umpk.Protocol.Java;

// With an argument, resolve one version and stop. Without one, list them all.
if (args.Length == 1)
{
    string want = args[0];
    if (int.TryParse(want, out int protocol))
    {
        if (JavaVersions.TryGetByProtocol(protocol, out JavaVersion byProtocol))
        {
            PrintOne(byProtocol);
            return 0;
        }

        Console.Error.WriteLine($"Protocol {protocol} is not one UMPK ships data for.");
        return 1;
    }

    if (JavaVersions.TryGetByName(want, out JavaVersion byName))
    {
        PrintOne(byName);
        return 0;
    }

    Console.Error.WriteLine($"Version name '{want}' is not one UMPK knows. Try 772 or 1.21.8.");
    return 1;
}

if (args.Length > 1)
{
    Console.Error.WriteLine("Usage: VersionTable [protocol|version]");
    return 2;
}

// All holds one entry per protocol, 50 of them. Several release names share one protocol, so the named properties outnumber the list. Ten 1.8.x releases share protocol 47, for example, and All still holds it once.
Console.WriteLine($"Supported protocols: {JavaVersions.All.Count}");
Console.WriteLine();
Console.WriteLine("Protocol  Version");
foreach (JavaVersion version in JavaVersions.All)
{
    Console.WriteLine($"{version.Version.Protocol,8}  {version.Version.Name}");
}

Console.WriteLine();

// The two lookups. By number is what a status ping gives you. By name is what a human types. Both answer false instead of throwing.
Console.WriteLine("Lookups:");
if (JavaVersions.TryGetByProtocol(772, out JavaVersion v772))
{
    Console.WriteLine($"  protocol 772 resolves to {v772.Version.Name}");
}

if (JavaVersions.TryGetByName("1.21.8", out JavaVersion vName))
{
    Console.WriteLine($"  name 1.21.8 resolves to protocol {vName.Version.Protocol}");
}

// Registries come from the same data. Item and block ids decode against them, which is why a client installs them before it joins. This line proves the table for the newest protocol loads without a connection.
var registries = JavaGameData.Registries(JavaVersions.All[^1].Version.Protocol);
Console.WriteLine($"  newest protocol block table holds {registries.Blocks.Count} blocks");

return 0;

static void PrintOne(JavaVersion version)
{
    Console.WriteLine($"Version:  {version.Version.Name}");
    Console.WriteLine($"Protocol: {version.Version.Protocol}");
    Console.WriteLine($"Chat signing era: {version.Features.ChatSigning}");
}
