using System.Text;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>Where <c>minecraft:set_score</c> stops being the action-based packet and becomes the 1.20.3 record. Protocols 477-764 use the action-based form; protocol 765 begins the modern record.</summary>
/// <remarks>
/// <para>The legacy frame reads a 40-character UTF string, a VarInt method ordinal, a 16-character UTF string, and a conditional VarInt. This frame is byte-identical through 1.19. The 1.20.4 form is the record: two UTF strings, a VarInt, a nullable component, and a nullable number format, and the REMOVE action it dropped moved out into the separate <c>reset_score</c> packet. So the boundary is 1.20.3 (protocol 765), not 1.14 (477).</para>
/// <para>Neither existing gate could see this. The registration fixture records a codec either way, and a round trip through the record agrees with itself perfectly. The pins here are the bound RECORD TYPE resolved through the registrar, exact frame bytes, a frame-length difference, and cross-era rejection in both directions.</para>
/// </remarks>
public class ScoreUpdateCodecTests
{
    private const string SetScore = "minecraft:set_score";

    private const int P477 = 477;   // 1.14: first protocol represented by the modern record
    private const int P498 = 498;   // 1.14.4
    private const int P754 = 754;   // 1.16.5
    private const int P759 = 759;   // 1.19
    private const int P763 = 763;   // 1.20/1.20.1
    private const int P764 = 764;   // 1.20.2: the last action-based protocol
    private const int P765 = 765;   // 1.20.3: the record arrives
    private const int P769 = 769;   // 1.21.4

    private static ClientboundLegacySetScorePacket ActionPacket { get; } =
        new("owner", LegacyScoreAction.Change, "obj", 5);

    private static ClientboundSetScorePacket RecordPacket { get; } =
        new("owner", "obj", 5, null, null);

    /// <summary>The registrar must resolve the ACTION record on every protocol from 47 to 764 and the 1.20.3 record from 765 on. Both packet types carry the same <c>minecraft:set_score</c> identifier, so the bound <see cref="PacketType"/> instance is what separates them, and only resolving by protocol number can show it.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="legacy">True when the era should resolve the action-based record.</param>
    [Theory]
    [InlineData(47, true)]
    [InlineData(404, true)]
    [InlineData(P477, true)]
    [InlineData(P498, true)]
    [InlineData(P754, true)]
    [InlineData(P759, true)]
    [InlineData(P764, true)]
    [InlineData(P765, false)]
    [InlineData(P769, false)]
    [InlineData(776, false)]
    public void SetScore_ResolvesTheActionRecordThrough764(int protocol, bool legacy)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetScore);

        Assert.Same(
            legacy ? UiPackets.Clientbound.LegacySetScore : UiPackets.Clientbound.SetScore,
            bound.Type);
    }

    /// <summary>The action band's exact frame: <c>owner</c>, the action VarInt, <c>objective</c>, then the value. Asserted byte for byte because the two forms differ by a single interior VarInt and nothing about the shape of the frame announces which one it is.</summary>
    /// <param name="protocol">The protocol.</param>
    [Theory]
    [InlineData(P477)]
    [InlineData(P498)]
    [InlineData(P754)]
    [InlineData(P759)]
    [InlineData(P764)]
    public void SetScore_ActionBandFrameIsOwnerActionObjectiveValue(int protocol)
    {
        byte[] frame = BoundCodec.At(protocol, PacketFlow.Clientbound, SetScore).Encode(ActionPacket);

        Assert.Equal(Bytes(5, "owner", 0, 3, "obj", 5), frame);
    }

    /// <summary>A REMOVE action omits the value entirely, which is the one thing the 1.20.3 record structurally cannot express (1.20.3 moved removal into <c>reset_score</c>). One byte shorter, and a decode on the action band recovers the action a consumer needs to erase the line.</summary>
    [Fact]
    public void SetScore_RemoveActionOmitsTheValueOnTheActionBand()
    {
        BoundPacketCodec bound = BoundCodec.At(P763, PacketFlow.Clientbound, SetScore);
        byte[] frame = bound.Encode(new ClientboundLegacySetScorePacket("owner", LegacyScoreAction.Remove, "obj", 0));

        Assert.Equal(Bytes(5, "owner", 1, 3, "obj"), frame);

        var back = (ClientboundLegacySetScorePacket)bound.DecodeFrame(frame);
        Assert.Equal(LegacyScoreAction.Remove, back.Action);
    }

    /// <summary>The two forms have different lengths for the same logical score: 764 writes 12 bytes and 765 writes 13 because two nullable-absent booleans replace the action VarInt.</summary>
    [Fact]
    public void SetScore_FormsDifferInLengthAcrossTheBoundary()
    {
        byte[] action = BoundCodec.At(P764, PacketFlow.Clientbound, SetScore).Encode(ActionPacket);
        byte[] record = BoundCodec.At(P765, PacketFlow.Clientbound, SetScore).Encode(RecordPacket);

        Assert.Equal(12, action.Length);
        Assert.Equal(13, record.Length);
    }

    /// <summary>Cross-era rejection, both directions. A 764 frame read as the record runs the objective name off the end of the buffer; a 765 frame read as the action packet does the same in the other direction.</summary>
    /// <param name="from">The protocol that builds the frame.</param>
    /// <param name="to">The protocol that must refuse it.</param>
    [Theory]
    [InlineData(P764, P765)]
    [InlineData(P765, P764)]
    public void SetScoreFrame_DoesNotCrossTheRecordBoundary(int from, int to)
    {
        object packet = from == P764 ? ActionPacket : RecordPacket;
        byte[] frame = BoundCodec.At(from, PacketFlow.Clientbound, SetScore).Encode(packet);

        Assert.ThrowsAny<Exception>(() => BoundCodec.At(to, PacketFlow.Clientbound, SetScore).DecodeFrame(frame));
    }

    // Builds an expected frame from alternating VarInt bytes and UTF-8 strings written raw (every value used here is a single-byte VarInt, so the expectation stays readable).
    private static byte[] Bytes(params object[] parts)
    {
        var bytes = new List<byte>();
        foreach (object part in parts)
        {
            if (part is int value)
            {
                bytes.Add((byte)value);
                continue;
            }

            bytes.AddRange(Encoding.UTF8.GetBytes((string)part));
        }

        return [.. bytes];
    }
}
