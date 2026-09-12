using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The JoinGame / ClientboundLogin packet has named era members built from one helper with explicit <see cref="JoinGameWire"/> values.</summary>
/// <remarks>
/// 26.2 adds a trailing online-mode boolean before enforces-secure-chat.
/// <para>1.16-1.20.1 carry their registry contents in this packet rather than in the configuration phase. That blob reaches the client as <c>ClientboundLoginPacket.JoinGameRegistry</c>, read by <c>ConnectionApplier</c>; nothing on the descriptor declares it.</para>
/// </remarks>
public static partial class JoinGameCodecs
{
    /// <summary>The pre-1.16 numeric dimension ids as vanilla resource keys. 47-578 (1.8-1.15.2) name the dimension with a signed int and carry no resource key at all, so the synthesized <c>CommonPlayerSpawnInfo</c> has to derive one: a client that joins straight into the nether or the end otherwise starts with the OVERWORLD's height bounds and skylight, which mis-sizes every chunk column decoded afterwards. The closed set is exactly three values (vanilla <c>DimensionType.getById</c>); anything else is a modded id with no known bounds, and the overworld default is the same fallback the respawn path uses.</summary>
    internal static string LegacyDimensionName(int dimension) => dimension switch
    {
        -1 => "minecraft:the_nether",
        1 => "minecraft:the_end",
        _ => "minecraft:overworld",
    };

    /// <summary>The decoded 1.20.2+ JoinGame header fields (see <see cref="WriteModernJoinHeader"/>).</summary>
    private readonly record struct ModernJoinHeader(
        bool Hardcore,
        string[] Dimensions,
        int MaxPlayers,
        int ViewDistance,
        int SimulationDistance,
        bool ReducedDebugInfo,
        bool ShowDeathScreen,
        bool DoLimitedCrafting);
}
