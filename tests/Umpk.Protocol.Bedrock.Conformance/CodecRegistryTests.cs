using Xunit;

namespace Umpk.Protocol.Bedrock.Conformance;

public sealed class CodecRegistryTests
{
    [Theory]
    [InlineData(322)]
    public void UnresolvedMovementPacketsRemainUnbound(int packetId)
    {
        BedrockSessionCodecContext context = CreateContext(BedrockPacketDirection.Clientbound);
        var member = new BedrockBatchMember(new BedrockPacketHeader(packetId, default, default), []);

        Assert.Throws<BedrockCodecException>(() => Bedrock2169CodecRegistry.Decode(member, context));
    }

    [Fact]
    public void MovePlayerHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("010000084100008242000050C0000000000000B4420000B44200010000D209");

        BedrockMovePlayerPacket packet = Assert.IsType<BedrockMovePlayerPacket>(Decode(19, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(1UL, packet.RuntimeEntityId);
        Assert.Equal(BedrockMovePlayerMode.Normal, packet.Mode);
        Assert.Null(binding.ExpectedDirection);
        Assert.Equal(payload, BedrockActorCodecs.Encode(packet));
    }

    [Fact]
    public void RemoveEntityHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("9007");

        BedrockRemoveEntityPacket packet = Assert.IsType<BedrockRemoveEntityPacket>(Decode(14, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(456L, packet.UniqueEntityId);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockActorCodecs.Encode(packet));
    }

    [Fact]
    public void PlayerLocationHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("0A0100");

        BedrockPlayerLocationPacket packet = Assert.IsType<BedrockPlayerLocationPacket>(Decode(326, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(5L, packet.EntityUniqueId);
        Assert.True(packet.Hide);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockActorCodecs.Encode(packet));
    }

    [Fact]
    public void MobEffectHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("0201140201B0092A00");

        BedrockMobEffectPacket packet = Assert.IsType<BedrockMobEffectPacket>(Decode(28, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(2UL, packet.RuntimeEntityId);
        Assert.Equal(BedrockMobEffectEvent.Add, packet.Event);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockObservationCodecs.Encode(packet));
    }

    [Fact]
    public void UpdateAttributesHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("0301000000000000A0410000A041000000000000A0410000A041106D696E6563726166743A6865616C74680007");

        BedrockUpdateAttributesPacket packet = Assert.IsType<BedrockUpdateAttributesPacket>(Decode(29, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(3UL, packet.RuntimeEntityId);
        Assert.Equal("minecraft:health", Assert.Single(packet.Attributes).Name);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockObservationCodecs.Encode(packet));
    }

    [Fact]
    public void MobEquipmentHasExactBidirectionalWitness()
    {
        byte[] payload = Convert.FromHexString("020700010000000000000000");

        BedrockMobEquipmentPacket packet = Assert.IsType<BedrockMobEquipmentPacket>(Decode(31, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(2UL, packet.RuntimeEntityId);
        Assert.Equal(7, packet.Item.NetworkId);
        Assert.Null(binding.ExpectedDirection);
        Assert.Equal(payload, BedrockObservationCodecs.Encode(packet));
    }

    [Fact]
    public void MobArmorEquipmentHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("03" + string.Concat(Enumerable.Repeat("0000000000000000", 5)));

        BedrockMobArmorEquipmentPacket packet = Assert.IsType<BedrockMobArmorEquipmentPacket>(Decode(32, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(3UL, packet.RuntimeEntityId);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockObservationCodecs.Encode(packet));
    }

    [Fact]
    public void SetEntityLinkHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("C80190030101010000003F");

        BedrockSetEntityLinkPacket packet = Assert.IsType<BedrockSetEntityLinkPacket>(Decode(41, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(100L, packet.Link.RiddenUniqueId);
        Assert.Equal(BedrockEntityLinkType.Rider, packet.Link.Type);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockObservationCodecs.Encode(packet));
    }

    [Fact]
    public void SubChunkResponseHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("00000000000000000000000000000100000006000000000000");

        BedrockSubChunkResponsePacket packet = Assert.IsType<BedrockSubChunkResponsePacket>(Decode(174, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.False(packet.CacheEnabled);
        Assert.Equal(0, packet.Dimension);
        BedrockSubChunkEntry entry = Assert.Single(packet.Entries);
        Assert.Equal(BedrockSubChunkResult.SuccessAllAir, entry.Result);
        Assert.Null(entry.Payload);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockSubChunkCodecs.EncodeResponse(packet));
    }

    [Fact]
    public void SubChunkRequestHasExactServerboundWitness()
    {
        byte[] payload = Convert.FromHexString("000201FE0380007F1000000004000000E0FFFFFF");

        BedrockSubChunkRequestPacket packet = Assert.IsType<BedrockSubChunkRequestPacket>(Decode(175, BedrockPacketDirection.Serverbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(0, packet.Dimension);
        Assert.Equal(2, packet.Offsets.Count);
        Assert.Equal(new BedrockSubChunkPosition(16, 4, -32), packet.Center);
        Assert.Equal(BedrockPacketDirection.Serverbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockSubChunkCodecs.EncodeRequest(packet));
    }

    [Fact]
    public void UpdateBlockHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("148001272A0200");

        BedrockUpdateBlockPacket packet = Assert.IsType<BedrockUpdateBlockPacket>(Decode(21, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(10, packet.X);
        Assert.Equal(64, packet.Y);
        Assert.Equal(-20, packet.Z);
        Assert.Equal(42u, packet.BlockRuntimeId);
        Assert.Equal(2u, packet.Flags);
        Assert.Equal(0u, packet.Layer);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockUpdateBlockCodecs.EncodeUpdateBlock(packet));
    }

    [Fact]
    public void UpdateSubChunkBlocksHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("000000010204060402050000");

        BedrockUpdateSubChunkBlocksPacket packet = Assert.IsType<BedrockUpdateSubChunkBlocksPacket>(Decode(172, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(0, packet.X);
        Assert.Equal(0, packet.Y);
        Assert.Equal(0, packet.Z);
        BedrockSubChunkBlockChange change = Assert.Single(packet.Blocks);
        Assert.Equal(1, change.X);
        Assert.Equal(2, change.Y);
        Assert.Equal(3, change.Z);
        Assert.Equal(4u, change.BlockRuntimeId);
        Assert.Equal(2u, change.Flags);
        Assert.Equal(5L, change.SyncedEntityId);
        Assert.Equal(BedrockSubChunkBlockTransition.None, change.Transition);
        Assert.Empty(packet.Extra);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockUpdateSubChunkBlocksCodecs.Encode(packet));
    }

    [Fact]
    public void InventoryContentHasExactBidirectionalWitness()
    {
        byte[] payload = Convert.FromHexString("0002010005000000010000000000000000000c000000000000000000");

        BedrockInventoryContentPacket packet = Assert.IsType<BedrockInventoryContentPacket>(Decode(49, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(0, packet.WindowId);
        Assert.Equal(2, packet.Items.Length);
        Assert.Equal(5, packet.Items[0].Count);
        Assert.Null(binding.ExpectedDirection);
        Assert.Equal(payload, BedrockInventoryCodecs.Encode(packet));
    }

    [Fact]
    public void InventorySlotHasPrimarySchemaBidirectionalWitness()
    {
        byte[] payload = Convert.FromHexString("000500000100050000000100");

        BedrockInventorySlotPacket packet = Assert.IsType<BedrockInventorySlotPacket>(Decode(50, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(0, packet.WindowId);
        Assert.Equal(5, packet.Slot);
        Assert.Null(packet.Container);
        Assert.Null(binding.ExpectedDirection);
        Assert.Equal(payload, BedrockInventoryCodecs.Encode(packet));
    }

    [Fact]
    public void ContainerOpenHasPrimarySchemaClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("01001480012701");

        BedrockContainerOpenPacket packet = Assert.IsType<BedrockContainerOpenPacket>(Decode(46, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(1, packet.WindowId);
        Assert.Equal(0, packet.WindowType);
        Assert.Equal(10, packet.X);
        Assert.Equal(64, packet.Y);
        Assert.Equal(-20, packet.Z);
        Assert.Equal(-1L, packet.EntityUniqueId);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockInventoryCodecs.Encode(packet));
    }

    [Fact]
    public void ContainerCloseHasPrimarySchemaBidirectionalWitness()
    {
        byte[] payload = Convert.FromHexString("010300");

        BedrockContainerClosePacket packet = Assert.IsType<BedrockContainerClosePacket>(Decode(47, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(1, packet.WindowId);
        Assert.Equal(3, packet.WindowType);
        Assert.False(packet.ServerSide);
        Assert.Null(binding.ExpectedDirection);
        Assert.Equal(payload, BedrockInventoryCodecs.Encode(packet));
    }

    [Fact]
    public void PlayerHotbarHasPrimarySchemaBidirectionalWitness()
    {
        byte[] payload = Convert.FromHexString("020001");

        BedrockPlayerHotbarPacket packet = Assert.IsType<BedrockPlayerHotbarPacket>(Decode(48, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(2, packet.SelectedSlot);
        Assert.Equal(0, packet.WindowId);
        Assert.True(packet.SelectSlot);
        Assert.Null(binding.ExpectedDirection);
        Assert.Equal(payload, BedrockInventoryCodecs.Encode(packet));
    }

    [Fact]
    public void PacketDirectionIsValidatedBeforePayloadDecode()
    {
        BedrockSessionCodecContext context = CreateContext(BedrockPacketDirection.Clientbound);
        var member = new BedrockBatchMember(new BedrockPacketHeader(1, default, default), []);

        Assert.Throws<BedrockCodecException>(() => Bedrock2169CodecRegistry.Decode(member, context));
    }

    [Fact]
    public void PlayStatusPlayerSpawnHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("00000003");

        PlayStatusPacket packet = Assert.IsType<PlayStatusPacket>(Decode(2, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(PlayStatusCode.PlayerSpawn, packet.Status);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockBootstrapCodecs.Encode(packet));
    }

    [Fact]
    public void RequestChunkRadiusHasExactServerboundWitness()
    {
        byte[] payload = Convert.FromHexString("0120");

        RequestChunkRadiusPacket packet = Assert.IsType<RequestChunkRadiusPacket>(Decode(69, BedrockPacketDirection.Serverbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(-1, packet.Radius);
        Assert.Equal((byte)32, packet.MaximumRadius);
        Assert.Equal(BedrockPacketDirection.Serverbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockBootstrapCodecs.Encode(packet));
    }

    [Fact]
    public void ChunkRadiusUpdateHasExactClientboundWitness()
    {
        byte[] payload = Convert.FromHexString("10");

        ChunkRadiusUpdatePacket packet = Assert.IsType<ChunkRadiusUpdatePacket>(Decode(70, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(8, packet.Radius);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockBootstrapCodecs.Encode(packet));
    }

    [Fact]
    public void SetLocalPlayerAsInitializedHasExactServerboundWitness()
    {
        byte[] payload = Convert.FromHexString("8080808010");

        SetLocalPlayerAsInitializedPacket packet = Assert.IsType<SetLocalPlayerAsInitializedPacket>(Decode(113, BedrockPacketDirection.Serverbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(0x1_0000_0000UL, packet.RuntimeEntityId);
        Assert.Equal(BedrockPacketDirection.Serverbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockBootstrapCodecs.Encode(packet));
    }

    [Fact]
    public void ClientCacheStatusHasExactServerboundWitness()
    {
        byte[] payload = Convert.FromHexString("01");

        ClientCacheStatusPacket packet = Assert.IsType<ClientCacheStatusPacket>(Decode(129, BedrockPacketDirection.Serverbound, payload, out Bedrock2169CodecBinding binding));

        Assert.True(packet.Enabled);
        Assert.Equal(BedrockPacketDirection.Serverbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockBootstrapCodecs.Encode(packet));
    }

    [Theory]
    [InlineData(2, "00000003", BedrockPacketDirection.Serverbound)]
    [InlineData(69, "0120", BedrockPacketDirection.Clientbound)]
    [InlineData(70, "10", BedrockPacketDirection.Serverbound)]
    [InlineData(113, "8080808010", BedrockPacketDirection.Clientbound)]
    [InlineData(129, "01", BedrockPacketDirection.Clientbound)]
    public void JoinPacketDirectionMismatchFails(int packetId, string payloadHex, BedrockPacketDirection direction)
    {
        BedrockSessionCodecContext context = CreateContext(direction);
        var member = new BedrockBatchMember(new BedrockPacketHeader(packetId, default, default), Convert.FromHexString(payloadHex));

        Assert.Throws<BedrockCodecException>(() => Bedrock2169CodecRegistry.Decode(member, context));
    }

    private static object Decode(int packetId, BedrockPacketDirection direction, byte[] payload, out Bedrock2169CodecBinding binding)
    {
        BedrockSessionCodecContext context = CreateContext(direction);
        var member = new BedrockBatchMember(new BedrockPacketHeader(packetId, default, default), payload);
        return Bedrock2169CodecRegistry.Decode(member, context, out binding);
    }

    private static BedrockSessionCodecContext CreateContext(BedrockPacketDirection direction)
    {
        var profile = new BedrockProtocolProfile(
            new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169"));
        return new BedrockSessionCodecContext(
            new BedrockCodecContext(profile, direction, default, default),
            BedrockRegistrySnapshot.Empty,
            BedrockCodecLimits.Default);
    }
}
