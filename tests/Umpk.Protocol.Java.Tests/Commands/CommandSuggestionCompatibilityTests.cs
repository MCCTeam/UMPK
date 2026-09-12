using System.Buffers;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;
using static Umpk.Protocol.Java.Tests.Support.LiteralFrame;

namespace Umpk.Protocol.Java.Tests.Commands;

/// <summary>Incompatible command-suggestion wire layouts reject one another.</summary>
public sealed class CommandSuggestionCompatibilityTests
{
    [Fact]
    public void CommandSuggestions_PlainAndRangedFrames_RejectEachOther()
    {
        byte[] legacy = Cat(VarInt(2), Str("/give"), Str("/gamemode"));
        byte[] brigadier = Cat(VarInt(9), VarInt(4), VarInt(6), VarInt(1), Str("stone"), [0]);

        Rejects(Clientbound(393, "command_suggestions"), legacy, "1.13 is the Brigadier suggestion list");
        Rejects(Clientbound(340, "command_suggestions"), brigadier, "1.12.2 is a plain list of match strings");
    }

    [Fact]
    public void ServerboundCommandSuggestion_AlternateWireShapes_AreRejected()
    {
        byte[] legacy = Cat(Str("/give @p sto"), [1], [0]);
        byte[] brigadier = Cat(VarInt(11), Str("/give @p sto"));

        Rejects(Serverbound(393, "command_suggestion"), legacy, "1.13 sends a transaction id first");
        Rejects(Serverbound(340, "command_suggestion"), brigadier, "1.12.2 sends the text first");
    }
}
