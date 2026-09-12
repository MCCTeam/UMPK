using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        /// <summary>Legacy 1.8 title (<c>minecraft:title</c>, 47; single packet with an action).</summary>
        public static readonly PacketType<ClientboundLegacyTitlePacket> LegacyTitle =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("title"));
    }
}

/// <summary>Legacy 1.8 title (47): a single packet with an action. Title/subtitle carry a component; times carries three big-endian ints; clear/reset carry nothing.</summary>
public sealed record ClientboundLegacyTitlePacket(
    LegacyTitleAction Action,
    Component? Text,
    int FadeIn,
    int Stay,
    int FadeOut) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.LegacyTitle;
}

/// <summary>The 1.8 title action (title=0, subtitle=1, times=2, clear=3, reset=4).</summary>
public enum LegacyTitleAction : byte
{
    /// <summary>Set the title component.</summary>
    Title = 0,

    /// <summary>Set the subtitle component.</summary>
    Subtitle = 1,

    /// <summary>Set the animation times.</summary>
    Times = 2,

    /// <summary>Clear (hide) the title.</summary>
    Clear = 3,

    /// <summary>Reset the title.</summary>
    Reset = 4,
}
