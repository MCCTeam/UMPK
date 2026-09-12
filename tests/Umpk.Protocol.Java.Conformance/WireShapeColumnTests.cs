using System.Text.RegularExpressions;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The third column of the codec-identity pin, and the properties that make it worth reading.</summary>
/// <remarks>
/// <para>The identity column is the bind site's own source text, so a rename sweep moves every line that names the renamed member and a mis-rebinding moves the same lines the same way. That collision is the reason the shape column exists: it is built from the codec OBJECT, so a rename cannot reach it, while a rebinding across two codecs whose shapes differ moves it by construction.</para>
/// <para>The guarantee is scoped, not absolute, and these tests pin the scope rather than the claim. Two codecs that declare the same field list are twins for this column and always will be; what is asserted here is that the DERIVED half (era shapes and era tables) separates the neighbouring eras where the recurring misbinding actually lives, and that the covered set never shrinks.</para>
/// </remarks>
public sealed class WireShapeColumnTests
{
    /// <summary>The floor on how many packet families carry a real field list. It ratchets upward as authored shapes land; it never comes down, because a family losing its token is a column losing its teeth.</summary>
    private const int CoveredFamilyFloor = 45;

    public static IEnumerable<object[]> Protocols =>
        JavaVersions.All.Select(v => new object[] { v.Version.Protocol }).Distinct();

    /// <summary>The column's whole point: the identity text is the caller's, the shape is the codec's, and no spelling of the former can reach the latter.</summary>
    [Fact]
    public void RenamingACodecAtItsBindSite_DoesNotMoveTheShapeToken()
    {
        PacketCodec<ServerboundPlayKeepAlivePacket> codec = PlayKeepAliveCodecs.ServerV1_12_2;
        BoundPacketCodec before = BoundPacketCodec.Create(0x0F, PlayPackets.Serverbound.KeepAlive, codec, "PlayKeepAliveCodecs.ServerV1_12_2");
        BoundPacketCodec after = BoundPacketCodec.Create(0x0F, PlayPackets.Serverbound.KeepAlive, codec, "KeepAlive.ServerLong");

        Assert.NotEqual(before.CodecIdentity, after.CodecIdentity);
        Assert.Equal(before.Shape.Token, after.Shape.Token);
        Assert.NotEqual(WireShape.Opaque.Token, after.Shape.Token);
    }

    /// <summary>The complement, on the families whose token is derived from an era shape or an era table: the two eras really are separated by the column, so a binding that reaches one protocol back moves it. The pairs are written here as protocol numbers rather than read off the descriptors, so a dataset that lost an era cannot make this pass by agreeing with itself.</summary>
    [Theory]
    [InlineData("minecraft:level_chunk_with_light", PacketFlow.Clientbound, 770, 775)]  // 26.1 adds the per-section fluid count
    [InlineData("minecraft:level_chunk_with_light", PacketFlow.Clientbound, 764, 768)]  // the long-array framing loses its cross-check
    [InlineData("minecraft:level_chunk_with_light", PacketFlow.Clientbound, 735, 755)]  // the section mask becomes a BitSet long array
    [InlineData("minecraft:container_set_slot", PacketFlow.Clientbound, 769, 770)]      // the component ordering and the dialect both move
    [InlineData("minecraft:container_set_slot", PacketFlow.Clientbound, 770, 771)]      // 1.21.6 keeps the ordering and changes attribute_modifiers
    [InlineData("minecraft:container_set_slot", PacketFlow.Clientbound, 771, 773)]      // 1.21.9 re-shapes profile and the two entity-data payloads
    [InlineData("minecraft:container_set_slot", PacketFlow.Clientbound, 774, 775)]      // 26.1 unwraps the holders and templates the nested stack
    [InlineData("minecraft:login", PacketFlow.Clientbound, 735, 751)]                   // the dimension type becomes an inline NBT compound
    [InlineData("minecraft:login", PacketFlow.Clientbound, 751, 757)]                   // 1.18 appends the simulation distance
    [InlineData("minecraft:set_player_team", PacketFlow.Clientbound, 764, 765)]         // components move from JSON to network NBT
    [InlineData("minecraft:set_player_team", PacketFlow.Clientbound, 769, 770)]         // and the interaction dialect moves at 1.21.5
    [InlineData("minecraft:hello", PacketFlow.Clientbound, 765, 766)]                   // the encryption request gains shouldAuthenticate
    [InlineData("minecraft:hello", PacketFlow.Serverbound, 758, 764)]                   // login-start gains the profile id
    [InlineData("minecraft:set_entity_data", PacketFlow.Clientbound, 769, 770)]         // the metadata component dialect and item table both move
    [InlineData("minecraft:set_entity_data", PacketFlow.Clientbound, 774, 775)]         // 26.1 re-shapes the item era the list carries
    [InlineData("minecraft:block_entity_data", PacketFlow.Clientbound, 756, 757)]       // the block-entity type becomes a registry VarInt
    [InlineData("minecraft:respawn", PacketFlow.Clientbound, 765, 766)]                 // the spawn info's dimension type stops being a string
    [InlineData("minecraft:respawn", PacketFlow.Clientbound, 767, 768)]                 // and 1.21.2 appends the sea level
    public void ABindingReachingIntoTheNeighbouringWireLayout_MovesTheShapeToken(string identifier, PacketFlow flow, int left, int right)
    {
        var id = Identifier.Parse(identifier);
        string leftToken = ShapeTokenOf(left, flow, id);
        string rightToken = ShapeTokenOf(right, flow, id);

        Assert.NotEqual(WireShape.Opaque.Token, leftToken);
        Assert.NotEqual(WireShape.Opaque.Token, rightToken);
        Assert.NotEqual(leftToken, rightToken);
    }

    /// <summary>The pin appends the token as the last whitespace-separated field of a line, so a token carrying whitespace would silently split the column and defeat the additive check that reads it back.</summary>
    [Fact]
    public void EveryShapeToken_IsOneWhitespaceFreeField()
    {
        foreach (object[] row in Protocols)
        {
            Assert.True(JavaVersions.TryGetByProtocol((int)row[0], out JavaVersion version));
            foreach ((ProtocolPhase phase, PacketFlow flow, int wireId, BoundPacketCodec entry) in Walk(version.Protocol))
            {
                string token = entry.Shape.Token;
                Assert.False(string.IsNullOrEmpty(token), $"{phase}/{flow} 0x{wireId:X2} {entry.Type.Id} has an empty shape token.");
                Assert.DoesNotContain(token, static c => char.IsWhiteSpace(c));
            }
        }
    }

    /// <summary>The item-component digest is over the era's <c>(wire id, identifier, component type id)</c> rows and its layout axes, never over a codec's C# type name, so a component codec class can be renamed without moving any pin line. A digest is eight hex characters; a name could not survive the rendering even if one reached the input.</summary>
    [Theory]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(776)]
    public void TheComponentTableToken_RendersAsADigest(int protocol)
    {
        string token = ShapeTokenOf(protocol, PacketFlow.Clientbound, Identifier.Parse("minecraft:container_set_slot"));
        Match digest = Regex.Match(token, "components/([0-9a-f]{8})$");
        Assert.True(digest.Success, $"protocol {protocol} container_set_slot shape token is '{token}'.");
    }

    /// <summary>The stated coverage number. A family counts when at least one of its bindings anywhere in the supported range declares a field list; the floor is a ratchet and the message carries the real total, so raising it is a one-line edit with the evidence in the failure.</summary>
    [Fact]
    public void TheShapeColumn_CoversAtLeastTheRatchetedFamilyCount()
    {
        var covered = new HashSet<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id)>();
        var all = new HashSet<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id)>();
        foreach (object[] row in Protocols)
        {
            Assert.True(JavaVersions.TryGetByProtocol((int)row[0], out JavaVersion version));
            foreach ((ProtocolPhase phase, PacketFlow flow, int _, BoundPacketCodec entry) in Walk(version.Protocol))
            {
                if (!entry.IsImplemented)
                    continue;

                all.Add((phase, flow, entry.Type.Id));
                if (entry.Shape.Token != WireShape.Opaque.Token)
                    covered.Add((phase, flow, entry.Type.Id));

            }
        }

        Assert.True(
            covered.Count >= CoveredFamilyFloor,
            $"{covered.Count} of {all.Count} implemented families carry a field list; the ratchet is {CoveredFamilyFloor}.");
    }

    private static string ShapeTokenOf(int protocol, PacketFlow flow, Identifier identifier)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        foreach ((ProtocolPhase _, PacketFlow entryFlow, int _, BoundPacketCodec entry) in Walk(version.Protocol))
            if (entryFlow == flow && entry.Type.Id == identifier && entry.IsImplemented)
                return entry.Shape.Token;

        Assert.Fail($"protocol {protocol} has no implemented {flow} binding for {identifier}.");
        return string.Empty;
    }

    private static IEnumerable<(ProtocolPhase Phase, PacketFlow Flow, int WireId, BoundPacketCodec Entry)> Walk(
        ProtocolDescriptor descriptor)
    {
        foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
            foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
            {
                if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                    continue;

                foreach ((int wireId, PacketType _) in registry.Packets)
                    if (registry.TryGetInbound(wireId, out BoundPacketCodec entry))
                        yield return (phase, flow, wireId, entry);

            }

    }
}
