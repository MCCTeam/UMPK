using Xunit;

namespace Umpk.DataGen.Tests;

/// <summary>The layout reader, over a synthetic fixture tree small enough to state the expected field list in the test instead of deriving it from the same parse being checked.</summary>
/// <remarks>The cases are the three shapes vanilla actually writes: a <c>StreamCodec.composite</c>, a <c>Packet.codec</c> over a hand-written <c>write</c>, and a <c>StreamCodec.unit</c> body-less packet. The composite is the one whose codec argument is itself a call with commas in it, which is what a naive split on commas gets wrong.</remarks>
public sealed class LayoutOracleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("umpk-layout-oracle").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Read_StatesTheFieldsOfEachCodecShape()
    {
        string tree = Tree("1.99");
        Packets(tree, """
            public class GamePacketTypes {
               public static final PacketType<ClientboundSetHealthPacket> CLIENTBOUND_SET_HEALTH = createClientbound("set_health");
               public static final PacketType<ClientboundSetTimePacket> CLIENTBOUND_SET_TIME = createClientbound("set_time");
               public static final PacketType<ClientboundPingPacket> CLIENTBOUND_PING = createClientbound("ping");
            }
            """);
        Packet(tree, "ClientboundSetHealthPacket", """
            public class ClientboundSetHealthPacket implements Packet<ClientGamePacketListener> {
               public static final StreamCodec<FriendlyByteBuf, ClientboundSetHealthPacket> STREAM_CODEC = Packet.codec(
                  ClientboundSetHealthPacket::write, ClientboundSetHealthPacket::new
               );

               private void write(final FriendlyByteBuf output) {
                  output.writeFloat(this.health);
                  output.writeVarInt(this.food);
               }
            }
            """);
        Packet(tree, "ClientboundSetTimePacket", """
            public record ClientboundSetTimePacket(long gameTime, Map<Holder<WorldClock>, ClockNetworkState> clockUpdates) {
               public static final StreamCodec<RegistryFriendlyByteBuf, ClientboundSetTimePacket> STREAM_CODEC = StreamCodec.composite(
                  ByteBufCodecs.LONG,
                  ClientboundSetTimePacket::gameTime,
                  ByteBufCodecs.map(HashMap::new, WorldClock.STREAM_CODEC, ClockNetworkState.STREAM_CODEC),
                  ClientboundSetTimePacket::clockUpdates,
                  ClientboundSetTimePacket::new
               );
            }
            """);
        Packet(tree, "ClientboundPingPacket", """
            public class ClientboundPingPacket {
               public static final StreamCodec<ByteBuf, ClientboundPingPacket> STREAM_CODEC = StreamCodec.unit(INSTANCE);
            }
            """);

        LayoutOracle.TreeLayouts layouts = LayoutOracle.Read(tree);

        Assert.Empty(layouts.Unparsed);
        Assert.Equal(
            ["ping", "set_health", "set_time"],
            layouts.Packets.Select(static packet => packet.Identifier).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["writeFloat(this.health)", "writeVarInt(this.food)"],
            Fields(layouts, "set_health"));
        Assert.Equal(
            [
                "gameTime: ByteBufCodecs.LONG",
                "clockUpdates: ByteBufCodecs.map(HashMap::new, WorldClock.STREAM_CODEC, ClockNetworkState.STREAM_CODEC)",
            ],
            Fields(layouts, "set_time"));
        Assert.Empty(Fields(layouts, "ping"));
    }

    [Fact]
    public void Read_NamesThePacketsItCouldNotParse()
    {
        string tree = Tree("1.99");
        Packets(tree, """
            public class GamePacketTypes {
               public static final PacketType<ClientboundBundlePacket> CLIENTBOUND_BUNDLE = createClientbound("bundle");
            }
            """);
        Packet(tree, "ClientboundBundlePacket", """
            public class ClientboundBundlePacket {
               public static final StreamCodec<ByteBuf, ClientboundBundlePacket> STREAM_CODEC = StreamCodec.ofMember(
                  ClientboundBundlePacket::encode, ClientboundBundlePacket::decode
               );
            }
            """);

        LayoutOracle.TreeLayouts layouts = LayoutOracle.Read(tree);

        // Silence here would be the dangerous outcome: a packet the reader cannot see looks exactly like a packet that did not change.
        Assert.Empty(layouts.Packets);
        Assert.Equal(["game Clientbound bundle (ClientboundBundlePacket)"], layouts.Unparsed);
        Assert.Equal(0, layouts.ParseRate);
    }

    [Fact]
    public void Read_DoesNotTreatANestedCodecAsThePacketCodec()
    {
        string tree = Tree("1.99");
        Packets(tree, """
            public class GamePacketTypes {
               public static final PacketType<ClientboundOuterPacket> CLIENTBOUND_OUTER = createClientbound("outer");
            }
            """);
        Packet(tree, "ClientboundOuterPacket", """
            public class ClientboundOuterPacket {
               public static final StreamCodec<ByteBuf, ClientboundOuterPacket> STREAM_CODEC = StreamCodec.ofMember(
                  ClientboundOuterPacket::encode, ClientboundOuterPacket::decode
               );

               public static final class Nested {
                  public static final StreamCodec<ByteBuf, Nested> STREAM_CODEC = StreamCodec.unit(INSTANCE);
               }
            }
            """);

        LayoutOracle.TreeLayouts layouts = LayoutOracle.Read(tree);

        Assert.Empty(layouts.Packets);
        Assert.Equal(["game Clientbound outer (ClientboundOuterPacket)"], layouts.Unparsed);
    }

    [LinuxFact]
    public void Read_DoesNotFollowDirectorySymlinkCycles()
    {
        string tree = Tree("1.99");
        Packets(tree, """
            public class GamePacketTypes {
               public static final PacketType<ClientboundPingPacket> CLIENTBOUND_PING = createClientbound("ping");
            }
            """);
        Packet(tree, "ClientboundPingPacket", Written("output.writeInt(this.value);"));

        string game = Path.Combine(tree, "net", "minecraft", "network", "protocol", "game");
        string cycle = Path.Combine(game, "cycle");
        CreateDirectorySymlink(cycle, game);
        try
        {
            LayoutOracle.TreeLayouts layouts = LayoutOracle.Read(tree);

            Assert.Single(layouts.Packets);
            Assert.Empty(layouts.Unparsed);
        }
        finally
        {
            DeleteLink(cycle);
        }
    }

    [Fact]
    public void RepoRoot_DiscoversTheSolutionFromTheAssemblyLocation()
    {
        string expected = LocateRepoRoot();

        Assert.Equal(expected, LayoutOracle.RepoRoot(AppContext.BaseDirectory));
    }

    [Fact]
    public void Layouts_RejectsBaselineWithoutTheDiffSwitch()
    {
        Assert.Equal(2, DataGenCli.Run(["layouts", "--data", "not-needed", "766", "--against", "765"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Layouts_RejectsAnEmptyOrUnrecognizedBaseline(bool declareUnrecognizedPacket)
    {
        string oracleRoot = Path.Combine(_root, "oracle");
        string proposed = TreeAt(oracleRoot, "26.2");
        Packets(proposed, """
            public class GamePacketTypes {
               public static final PacketType<ClientboundPingPacket> CLIENTBOUND_PING = createClientbound("ping");
            }
            """);
        Packet(proposed, "ClientboundPingPacket", Written("output.writeInt(this.value);"));

        string baseline = TreeAt(oracleRoot, "26.1");
        if (declareUnrecognizedPacket)
        {
            Packets(baseline, """
                public class GamePacketTypes {
                   public static final PacketType<ClientboundBundlePacket> CLIENTBOUND_BUNDLE = createClientbound("bundle");
                }
                """);
            Packet(baseline, "ClientboundBundlePacket", """
                public class ClientboundBundlePacket {
                   public static final StreamCodec<ByteBuf, ClientboundBundlePacket> STREAM_CODEC = StreamCodec.ofMember(
                      ClientboundBundlePacket::encode, ClientboundBundlePacket::decode
                   );
                }
                """);
        }

        Assert.Equal(
            1,
            DataGenCli.Run([
                "layouts",
                "--data", DataRoot(),
                "776",
                "--diff-oracle",
                "--oracle", oracleRoot,
            ]));
    }

    [Fact]
    public void Report_NamesTheChangedAddedAndRemovedPackets()
    {
        string before = Tree("1.98");
        Packets(before, """
            public class GamePacketTypes {
               public static final PacketType<ClientboundLoginPacket> CLIENTBOUND_LOGIN = createClientbound("login");
               public static final PacketType<ClientboundGonePacket> CLIENTBOUND_GONE = createClientbound("gone");
            }
            """);
        Packet(before, "ClientboundLoginPacket", Written("output.writeInt(this.playerId);"));
        Packet(before, "ClientboundGonePacket", Written("output.writeInt(this.id);"));

        string after = Tree("1.99");
        Packets(after, """
            public class GamePacketTypes {
               public static final PacketType<ClientboundLoginPacket> CLIENTBOUND_LOGIN = createClientbound("login");
               public static final PacketType<ClientboundFreshPacket> CLIENTBOUND_FRESH = createClientbound("fresh");
            }
            """);
        Packet(after, "ClientboundLoginPacket", Written("output.writeInt(this.playerId);\n      output.writeBoolean(this.onlineMode);"));
        Packet(after, "ClientboundFreshPacket", Written("output.writeVarInt(this.value);"));

        string report = LayoutOracle.Report(LayoutOracle.Read(before), LayoutOracle.Read(after));

        Assert.Contains("Vanilla source-to-source layout heuristic", report, StringComparison.Ordinal);
        Assert.Contains("baseline 1.98-decompiled", report, StringComparison.Ordinal);
        Assert.DoesNotContain("codec the new protocol INHERITS", report, StringComparison.Ordinal);
        Assert.Contains("CHANGED game Clientbound minecraft:login", report, StringComparison.Ordinal);
        Assert.Contains("writeBoolean(this.onlineMode)", report, StringComparison.Ordinal);
        Assert.Contains("NEW game Clientbound minecraft:fresh", report, StringComparison.Ordinal);
        Assert.Contains("GONE game Clientbound minecraft:gone", report, StringComparison.Ordinal);
        Assert.Contains("3 packet(s) moved.", report, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Fields(LayoutOracle.TreeLayouts layouts, string identifier) =>
        layouts.Packets.Single(packet => packet.Identifier == identifier).Fields;

    private static string Written(string body) => $$"""
        public class Packet {
           public static final StreamCodec<FriendlyByteBuf, Packet> STREAM_CODEC = Packet.codec(Packet::write, Packet::new);

           private void write(final FriendlyByteBuf output) {
              {{body}}
           }
        }
        """;

    private string Tree(string version)
    {
        return TreeAt(_root, version);
    }

    private static string TreeAt(string root, string version)
    {
        string game = Path.Combine(root, $"{version}-decompiled", "net", "minecraft", "network", "protocol", "game");
        Directory.CreateDirectory(game);
        return Path.Combine(root, $"{version}-decompiled");
    }

    private static void Packets(string tree, string text) => Packet(tree, "GamePacketTypes", text);

    private static void Packet(string tree, string type, string text) => File.WriteAllText(
        Path.Combine(tree, "net", "minecraft", "network", "protocol", "game", $"{type}.java"), text);

    private static string DataRoot()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            if (File.Exists(Path.Combine(cursor.FullName, "data", "java", "versions.json")))
                return Path.Combine(cursor.FullName, "data", "java");

            cursor = cursor.Parent;
        }

        throw new DirectoryNotFoundException("could not locate data/java from the test output directory");
    }

    private static string LocateRepoRoot()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            if (File.Exists(Path.Combine(cursor.FullName, "UMPK.sln")))
                return cursor.FullName;

            cursor = cursor.Parent;
        }

        throw new DirectoryNotFoundException("could not locate UMPK.sln from the test output directory");
    }

    private static void CreateDirectorySymlink(string link, string target)
        => Directory.CreateSymbolicLink(link, target);

    private static void DeleteLink(string link)
    {
        if (Directory.Exists(link))
            Directory.Delete(link);

    }
}

/// <summary>Runs the symlink case where the platform guarantees unprivileged symlink creation.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "The symlink-cycle regression runs on Linux; Windows discovery commonly lacks symlink privilege.";

    }
}
