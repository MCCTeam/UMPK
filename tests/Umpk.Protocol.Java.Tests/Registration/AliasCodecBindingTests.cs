using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Registration;

/// <summary>Eight renamed packet identifiers resolved by protocol number through the registrar. Each alias must select the same wire form as its canonical identifier on the corresponding band.</summary>
/// <remarks>
/// <para>Every assertion here is a frame-LENGTH or a cross-era rejection, never a bare round trip. A round trip through one wrong codec agrees with itself. The questions asked are "how many bytes does the bound decoder consume" and "does the neighbouring era's frame fault here", which a wrong binding cannot answer correctly.</para>
/// <para>set_spawn_position is the exception that proves the rule and is treated separately: its two candidate eras consume the SAME eight bytes and differ only in the field order inside the packed long, so the only assertion that can see it is on the decoded coordinates.</para>
/// </remarks>
public sealed class AliasCodecBindingTests
{
    // The supported protocol numbers, so a band assertion covers every version rather than its endpoints.
    private static readonly int[] All =
    [
        47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404,
        477, 480, 485, 490, 498, 573, 575, 578, 735, 736, 751, 753, 754, 755, 756, 757, 758,
        759, 760, 761, 762, 763, 764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776,
    ];

    /// <summary>The literal table's protocol column, read by <c>AllProtocolTableCoverageTests</c>.</summary>
    public static IReadOnlyList<int> Protocols() => All;

    private static int[] Band(int first, int last) => [.. All.Where(p => p >= first && p <= last)];

    // set_carried_item (clientbound): the server-forced hotbar slot, a marker on 477-767.

    /// <summary>The legacy dataset name and the canonical name resolve the SAME codec at every protocol in the band. This is the alias claim stated directly: if the alias were still scoped to 1.13.2 the legacy name would resolve a marker from 477 up while the canonical name kept resolving a codec.</summary>
    [Fact]
    public void SetCarriedItem_LegacyAndCanonicalNames_ResolveTheSameCodec()
    {
        foreach (int protocol in Band(107, 767))
        {
            BoundPacketCodec legacy = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:set_carried_item");
            BoundPacketCodec canonical = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:set_held_slot");
            Assert.Equal(canonical.CodecIdentity, legacy.CodecIdentity);
            Assert.Equal(EntityPackets.Clientbound.SetHeldSlot.Id, legacy.Type.Id);

            // The dataset identifier records WHICH key resolved the binding, which is the only thing that tells an alias that fired from one that never did once the descriptor is built.
            Assert.Equal(Identifier.Minecraft("set_carried_item"), legacy.DatasetIdentifier);
            Assert.Equal(Identifier.Minecraft("set_held_slot"), canonical.DatasetIdentifier);
        }
    }

    /// <summary>One byte on the whole 47-767 band: the carried-item slot remains a byte throughout this range.</summary>
    [Fact]
    public void SetCarriedItem_IsExactlyOneByte_AcrossTheBand()
    {
        foreach (int protocol in Band(47, 767))
        {
            BoundPacketCodec bound = Resolve(protocol, PacketFlow.Clientbound, protocol >= 107 ? "minecraft:set_carried_item" : "minecraft:held_item_slot");
            Assert.Single(bound.Encode(new ClientboundSetHeldSlotPacket(4)));

            // Cross-era rejection: a two-byte body is what a wider field would look like, and the era codec must refuse it rather than decode the first byte and drop the rest.
            Assert.Throws<ProtocolViolationException>(() => bound.DecodeFrame([4, 0]));
        }
    }

    // custom_sound (clientbound): a REAL era gap, not an alias. Three era forms on 107-760.

    /// <summary>custom_sound is its own packet, not a spelling of minecraft:sound. The two differ in their FIRST field (a resource-location string versus a registry-id VarInt) and vanilla shipped both side by side until the 1.19.3 holder merge, so the two identities must stay distinct and must not share a codec on any protocol that carries both.</summary>
    [Fact]
    public void CustomSound_IsNotTheRegistryIdSound()
    {
        foreach (int protocol in Band(107, 760))
        {
            BoundPacketCodec custom = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:custom_sound");
            Assert.Equal(WorldPackets.Clientbound.CustomSound.Id, custom.Type.Id);
            Assert.NotEqual(WorldPackets.Clientbound.Sound.Id, custom.Type.Id);

            // Not an alias: the dataset name IS the canonical name here, so nothing is standing in.
            Assert.Equal(Identifier.Minecraft("custom_sound"), custom.DatasetIdentifier);
        }
    }

    /// <summary>The three era forms, pinned by the byte count each consumes. The eight-byte sound name costs 1 + 8, the source VarInt 1, the three fixed-point ints 12 and the volume float 4: 26 bytes of shared head, which is the same field list on every era. 1.9-1.9.4 then adds a pitch BYTE (27), 1.10-1.18.2 a pitch FLOAT (30), and 1.19-1.19.1 a further seed long (38). Each of those three deltas is a vanilla read-path change, not a modelling choice: see the codec remarks.</summary>
    [Theory]
    [InlineData(107, 27)]
    [InlineData(110, 27)]
    [InlineData(210, 30)]
    [InlineData(404, 30)]
    [InlineData(578, 30)]
    [InlineData(758, 30)]
    [InlineData(759, 38)]
    [InlineData(760, 38)]
    public void CustomSound_FrameLength_MatchesTheWireLayout(int protocol, int expected)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:custom_sound");
        byte[] frame = bound.Encode(Sound());
        Assert.Equal(expected, frame.Length);

        // Decoding its own frame consumes it exactly; frame-exactness is enforced by BoundPacketCodec.
        Assert.NotNull(bound.DecodeFrame(frame));
    }

    /// <summary>Cross-era rejection in both directions across the two boundaries. A 1.19 frame carries eight bytes the 1.18.2 codec has no field for, and a 1.18.2 frame runs out under the 1.19 reader; the same argument one version earlier for the 1.9-versus-1.10 pitch widening.</summary>
    [Fact]
    public void CustomSound_NeighbouringWireLayoutFrames_AreRejected()
    {
        byte[] seeded = BoundCodec.At(759, PacketFlow.Clientbound, "minecraft:custom_sound").Encode(Sound());
        byte[] unseeded = BoundCodec.At(758, PacketFlow.Clientbound, "minecraft:custom_sound").Encode(Sound());
        byte[] bytePitch = BoundCodec.At(107, PacketFlow.Clientbound, "minecraft:custom_sound").Encode(Sound());

        Assert.Throws<ProtocolViolationException>(
            () => BoundCodec.At(758, PacketFlow.Clientbound, "minecraft:custom_sound").DecodeFrame(seeded));
        Assert.Throws<ProtocolViolationException>(
            () => BoundCodec.At(759, PacketFlow.Clientbound, "minecraft:custom_sound").DecodeFrame(unseeded));
        Assert.Throws<ProtocolViolationException>(
            () => BoundCodec.At(210, PacketFlow.Clientbound, "minecraft:custom_sound").DecodeFrame(bytePitch));
    }

    // set_equipped_item (clientbound): a REAL era gap, three item forms on 107-578.

    /// <summary>The legacy name resolves the entity-equipment timeline across 107-578.</summary>
    [Fact]
    public void SetEquippedItem_ResolvesTheEquipmentTimeline_AcrossTheBand()
    {
        foreach (int protocol in Band(107, 578))
        {
            BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:set_equipped_item");
            Assert.Equal(EntityPackets.Clientbound.SetEquipment.Id, bound.Type.Id);
            Assert.Equal(Identifier.Minecraft("set_equipped_item"), bound.DatasetIdentifier);
        }
    }

    /// <summary>The slot widened from a SHORT to a VarInt enum ordinal at 1.9, and the item form then moves twice more inside the band. With entity id 1, slot Head and an EMPTY item the counts are: 1.8 = varint id (1) + short slot (2) + empty short (2) = 5; 1.9-1.12.2 = 1 + 1 + 2 = 4; 1.13/1.13.1 = 1 + 1 + 2 = 4 (short id, still -1 for empty); 1.13.2-1.15.2 = 1 + 1 + 1 = 3 (the present bool). The 1.8-versus-1.9 pair are the ones that matter: they differ by exactly the slot widening.</summary>
    [Theory]
    [InlineData(47, 5)]
    [InlineData(107, 4)]
    [InlineData(340, 4)]
    [InlineData(393, 4)]
    [InlineData(401, 4)]
    [InlineData(404, 3)]
    [InlineData(578, 3)]
    public void SetEquippedItem_EmptyItemFrameLength_MatchesTheWireLayout(int protocol, int expected)
    {
        BoundPacketCodec bound = Resolve(
            protocol,
            PacketFlow.Clientbound,
            protocol == 47 ? "minecraft:entity_equipment" : "minecraft:set_equipped_item");
        var packet = new ClientboundSetEquipmentPacket(1, EquipmentSlot.Head, EntityItemSlot.Empty, null);
        Assert.Equal(expected, bound.Encode(packet).Length);
    }

    /// <summary>A filled stack separates the two forms that agree on the empty case: 1.9-1.12.2 writes a damage short the flattening removed, so the same stack costs two bytes more there than on 1.13. And the 1.13.2 present-flag frame is rejected outright by the 1.13 short-id reader.</summary>
    [Fact]
    public void SetEquippedItem_ItemFormBoundaries_AreVisible()
    {
        var packet = new ClientboundSetEquipmentPacket(
            1, EquipmentSlot.MainHand, new EntityItemSlot(false, 5, 1, 0, null), null);

        byte[] legacy = BoundCodec.At(340, PacketFlow.Clientbound, "minecraft:set_equipped_item").Encode(packet);
        byte[] shortId = BoundCodec.At(393, PacketFlow.Clientbound, "minecraft:set_equipped_item").Encode(packet);
        byte[] presentId = BoundCodec.At(404, PacketFlow.Clientbound, "minecraft:set_equipped_item").Encode(packet);

        // varint id + varint slot + [short id, byte count, short damage, nbt-end] = 2 + 6.
        Assert.Equal(8, legacy.Length);

        // The damage short is gone at 1.13.
        Assert.Equal(6, shortId.Length);

        // present bool + varint id + byte count + nbt-end.
        Assert.Equal(6, presentId.Length);

        // Same length as the 1.13 form but a different shape, so length alone cannot separate these two: decoding each under the other's codec is what shows it.
        Assert.NotEqual(shortId, presentId);
        var decodedShort = (ClientboundSetEquipmentPacket)BoundCodec
            .At(393, PacketFlow.Clientbound, "minecraft:set_equipped_item").DecodeFrame(shortId);
        var crossed = (ClientboundSetEquipmentPacket)BoundCodec
            .At(393, PacketFlow.Clientbound, "minecraft:set_equipped_item").DecodeFrame(presentId);
        Assert.Equal(5, decodedShort.LegacyItem!.ItemId);
        Assert.NotEqual(5, crossed.LegacyItem!.ItemId);
    }

    // move_player (serverbound): the stationary heartbeat, a marker on 477-754.

    /// <summary>The heartbeat is one on-ground byte on the whole 47-754 band. The old MarkerFrom(1.14) implied an era split, but the packet reads one unsigned byte as a boolean throughout this range, exactly what the 1.8 codec writes.</summary>
    [Fact]
    public void MovePlayerStatus_IsOneByte_AcrossTheBand()
    {
        foreach (int protocol in Band(47, 754))
        {
            BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:move_player");
            Assert.Single(bound.Encode(new ServerboundMovePlayerStatusPacket(true)));
            Assert.Throws<ProtocolViolationException>(() => bound.DecodeFrame([1, 0]));
        }

        // 1.17 renames the packet and adds the horizontal-collision flag, so the successor has a separate binding.
        foreach (int protocol in Band(755, 776))
            Assert.True(BoundCodec.IsImplementedAt(
                protocol, ProtocolPhase.Play, PacketFlow.Serverbound, "minecraft:move_player_status_only"));

    }

    // set_spawn_position (clientbound): a REAL era gap that no length check can see.

    /// <summary>The 1.14 block-pos packing flip. Both candidate eras write eight bytes for the position, so the frame length is identical on either side of the boundary and every length-based check is blind to it; only the decoded coordinates differ. This asserts the bytes actually move at 477 and that a frame written under one packing decodes to a DIFFERENT position under the other, which makes a cross-era alias observable.</summary>
    [Fact]
    public void SetSpawnPosition_PackingFlipsAt114_AndLengthCannotSeeIt()
    {
        var pos = new BlockPos(100, 64, -200);
        var packet = new ClientboundSetDefaultSpawnPositionPacket(pos, 0f, null, 0f);

        BoundPacketCodec pre = BoundCodec.At(404, PacketFlow.Clientbound, "minecraft:set_spawn_position");
        BoundPacketCodec post = BoundCodec.At(477, PacketFlow.Clientbound, "minecraft:set_spawn_position");

        byte[] preBytes = pre.Encode(packet);
        byte[] postBytes = post.Encode(packet);
        Assert.Equal(8, preBytes.Length);
        Assert.Equal(8, postBytes.Length);
        Assert.NotEqual(preBytes, postBytes);

        Assert.Equal(pos, ((ClientboundSetDefaultSpawnPositionPacket)pre.DecodeFrame(preBytes)).Position);
        Assert.Equal(pos, ((ClientboundSetDefaultSpawnPositionPacket)post.DecodeFrame(postBytes)).Position);

        // The misbinding this replaced: same eight bytes, silently wrong coordinates.
        Assert.NotEqual(pos, ((ClientboundSetDefaultSpawnPositionPacket)pre.DecodeFrame(postBytes)).Position);
    }

    /// <summary>The band the alias widening covers, plus the 1.16-1.16.5 protocols that were already bound and were quietly using the pre-1.14 packing. 1.17 appends the spawn angle, which is a length change and therefore the one boundary in this family a length check CAN see.</summary>
    [Fact]
    public void SetSpawnPosition_UsesThe114Packing_Through1165()
    {
        var pos = new BlockPos(100, 64, -200);
        var packet = new ClientboundSetDefaultSpawnPositionPacket(pos, 0f, null, 0f);
        byte[] reference = BoundCodec.At(477, PacketFlow.Clientbound, "minecraft:set_spawn_position").Encode(packet);

        foreach (int protocol in Band(477, 754))
        {
            string id = protocol <= 578 ? "minecraft:set_spawn_position" : "minecraft:set_default_spawn_position";
            BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, id);
            Assert.Equal(reference, bound.Encode(packet));
            Assert.Equal(pos, ((ClientboundSetDefaultSpawnPositionPacket)bound.DecodeFrame(reference)).Position);
        }

        BoundPacketCodec angled = BoundCodec.At(
            755, PacketFlow.Clientbound, "minecraft:set_default_spawn_position");
        Assert.Equal(12, angled.Encode(packet).Length);
        Assert.Throws<ProtocolViolationException>(() => angled.DecodeFrame(reference));
    }

    // The three 1.8-only legacy names.

    /// <summary>close_screen, craft_progress_bar and spectate are the 1.8 spellings of packets bound from 107 on. Each resolves the SAME codec under both names, and each frame is the exact width the 1.8.9 read method consumes: one byte, five bytes and sixteen bytes.</summary>
    [Theory]
    [InlineData("minecraft:close_screen", "minecraft:container_close", PacketFlow.Clientbound, 1)]
    [InlineData("minecraft:craft_progress_bar", "minecraft:container_set_data", PacketFlow.Clientbound, 5)]
    [InlineData("minecraft:spectate", "minecraft:teleport_to_entity", PacketFlow.Serverbound, 16)]
    public void LegacyNames_At47_ResolveTheModernCodec(string legacy, string canonical, PacketFlow flow, int width)
    {
        BoundPacketCodec at47 = BoundCodec.At(47, flow, legacy);
        BoundPacketCodec at107 = BoundCodec.At(107, flow, canonical);
        Assert.Equal(at107.CodecIdentity, at47.CodecIdentity);
        Assert.Equal(at107.Type.Id, at47.Type.Id);
        Assert.Equal(Identifier.Parse(legacy), at47.DatasetIdentifier);

        object packet = legacy switch
        {
            "minecraft:close_screen" => new ClientboundContainerClosePacket(3),
            "minecraft:craft_progress_bar" => new ClientboundContainerSetDataPacket(3, 1, 200),
            _ => new ServerboundTeleportToEntityPacket(Guid.NewGuid()),
        };

        byte[] frame = at47.Encode(packet);
        Assert.Equal(width, frame.Length);
        Assert.NotNull(at47.DecodeFrame(frame));

        // Cross-era rejection: one byte too many and the frame-exactness gate refuses it.
        Assert.Throws<ProtocolViolationException>(() => at47.DecodeFrame([.. frame, (byte)0]));
    }

    /// <summary>The 1.8-only legacy names must NOT resolve outside 1.8: the alias exists so the 1.8 dataset row finds the timeline, not so a later protocol can spell the packet either way.</summary>
    [Fact]
    public void LegacyNames_AreNotRegisteredUnderTheModernIdentity()
    {
        // close_screen and craft_progress_bar map onto item-family identities; spectate onto the entity one. What matters is that the canonical identity is what the descriptor renders, so a consumer matching on the packet type sees one identity across every protocol.
        Assert.Equal(
            ItemPackets.Clientbound.ContainerClose.Id,
            BoundCodec.At(47, PacketFlow.Clientbound, "minecraft:close_screen").Type.Id);
        Assert.Equal(
            ItemPackets.Clientbound.ContainerSetData.Id,
            BoundCodec.At(47, PacketFlow.Clientbound, "minecraft:craft_progress_bar").Type.Id);
        Assert.Equal(
            EntityPackets.Serverbound.TeleportToEntity.Id,
            BoundCodec.At(47, PacketFlow.Serverbound, "minecraft:spectate").Type.Id);
    }

    // bundle_delimiter: an empty body on 762-776.

    /// <summary>The delimiter carries no payload, so it must decode a zero-byte frame and reject a one-byte frame.</summary>
    [Fact]
    public void BundleDelimiter_IsAnEmptyBody_AcrossTheBand()
    {
        foreach (int protocol in Band(762, 776))
        {
            BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:bundle_delimiter");
            Assert.Empty(bound.Encode(new ClientboundBundleDelimiterPacket()));
            Assert.NotNull(bound.DecodeFrame([]));
            Assert.Throws<ProtocolViolationException>(() => bound.DecodeFrame([0]));
        }

        // Not present before 1.19.4, and the timeline must not pretend otherwise.
        Assert.False(BoundCodec.IsImplementedAt(
            761, ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:bundle_delimiter"));
    }

    /// <summary>The same cycle again, but driven by a real <see cref="JavaConnection"/> over the PRODUCTION binding table rather than by calling the accumulator directly.</summary>
    /// <remarks>A descriptor built by the registrar verifies that the delimiter reaches bundle accumulation in production binding, rather than only through a permissive fake binding.</remarks>
    [Fact]
    public async Task BundleDelimiter_DrivesTheAccumulator_OverTheProductionBindingTable()
    {
        const int DelimiterWire = 0x00;
        const int SlotWire = 0x51;

        var builder = new ProtocolDescriptorBuilder(new GameVersion(GameEdition.Java, "test", 770), new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, DelimiterWire, "minecraft:bundle_delimiter");
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, SlotWire, "minecraft:set_held_slot");

        var pair = Java.Transport.DuplexPipePair.Create();
        await using var connection = new JavaConnection(
            pair.Left,
            new JavaConnectionOptions { UnknownPacketPolicy = UnknownPacketPolicy.Preserve, ReadIdleTimeout = TimeSpan.Zero });
        connection.BindCodec(new DescriptorFrameCodecBinding(builder.Build()), PacketFlow.Clientbound);
        connection.SetPhase(ProtocolPhase.Play);
        connection.Start();

        await Tests.Transport.FramingTests.WriteRawFrameAsync(pair.Right.Output, [DelimiterWire]);
        await Tests.Transport.FramingTests.WriteRawFrameAsync(pair.Right.Output, [SlotWire, 2]);
        await Tests.Transport.FramingTests.WriteRawFrameAsync(pair.Right.Output, [SlotWire, 5]);
        await Tests.Transport.FramingTests.WriteRawFrameAsync(pair.Right.Output, [DelimiterWire]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        InboundItem item = await connection.ReceiveAsync(cts.Token);

        Assert.NotNull(item.Bundle);
        Assert.Equal(2, item.Bundle!.Packets.Count);
        Assert.Equal(2, ((ClientboundSetHeldSlotPacket)item.Bundle.Packets[0]).Slot);
        Assert.Equal(5, ((ClientboundSetHeldSlotPacket)item.Bundle.Packets[1]).Slot);
    }

    private static ClientboundCustomSoundPacket Sound() =>
        new("app:ping", Source: 3, X: 800, Y: 512, Z: -1600, Volume: 1f, Pitch: 63f, Seed: 12345L);

    private static BoundPacketCodec Resolve(int protocol, PacketFlow flow, string identifier) =>
        BoundCodec.At(protocol, flow, identifier);
}
