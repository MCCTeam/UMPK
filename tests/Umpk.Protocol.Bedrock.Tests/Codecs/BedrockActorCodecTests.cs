using System.Collections.Immutable;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockActorCodecTests
{
    [Fact]
    public void RemoveEntityUsesExactWireForm()
    {
        var packet = new BedrockRemoveEntityPacket(456L);
        byte[] expected = Convert.FromHexString("9007");

        Assert.Equal(expected, BedrockActorCodecs.Encode(packet));
        Assert.Equal(packet, BedrockActorCodecs.DecodeRemoveEntity(expected));
    }

    [Fact]
    public void SetEntityMotionUsesExactWireForm()
    {
        var packet = new BedrockSetEntityMotionPacket(2UL, new BedrockVector3(1f, 2f, -3f), 7UL);
        byte[] expected = Convert.FromHexString("020000803F00000040000040C007");

        Assert.Equal(expected, BedrockActorCodecs.Encode(packet));
        Assert.Equal(packet, BedrockActorCodecs.DecodeSetEntityMotion(expected));
    }

    [Fact]
    public void MovePlayerNormalUsesExactWireForm()
    {
        var packet = new BedrockMovePlayerPacket(
            1UL,
            new BedrockVector3(8.5f, 65f, -3.25f),
            0f,
            90f,
            90f,
            BedrockMovePlayerMode.Normal,
            true,
            0UL,
            null,
            null,
            1234UL);
        byte[] expected = Convert.FromHexString("010000084100008242000050C0000000000000B4420000B44200010000D209");

        Assert.Equal(expected, BedrockActorCodecs.Encode(packet));
        Assert.Equal(packet, BedrockActorCodecs.DecodeMovePlayer(expected));
    }

    [Fact]
    public void MovePlayerTeleportUsesBooleanPrefixedCause()
    {
        var packet = new BedrockMovePlayerPacket(
            2UL,
            new BedrockVector3(0f, 0f, 0f),
            0f,
            0f,
            0f,
            BedrockMovePlayerMode.Teleport,
            false,
            0UL,
            BedrockTeleportCause.Command,
            12,
            0UL);
        byte[] expected = Convert.FromHexString("02")
            .Concat(new byte[24], Convert.FromHexString("02000001"), Convert.FromHexString("030000000C000000"), Convert.FromHexString("00")).ToArray();

        Assert.Equal(expected, BedrockActorCodecs.Encode(packet));
        BedrockMovePlayerPacket decoded = BedrockActorCodecs.DecodeMovePlayer(expected);

        Assert.Equal(packet, decoded);
        Assert.Equal(BedrockTeleportCause.Command, decoded.TeleportCause);
        Assert.Equal(12, decoded.TeleportSourceEntityType);
    }

    [Fact]
    public void MovePlayerPreservesRuntimeIdsBeyondInt32()
    {
        var packet = new BedrockMovePlayerPacket(
            0x1_0000_0000UL,
            new BedrockVector3(0f, 0f, 0f),
            0f,
            0f,
            0f,
            BedrockMovePlayerMode.Normal,
            true,
            0UL,
            null,
            null,
            0x1_0000_0000UL);
        byte[] encoded = BedrockActorCodecs.Encode(packet);

        Assert.Equal(Convert.FromHexString("8080808010"), encoded[..5]);
        Assert.Equal(packet, BedrockActorCodecs.DecodeMovePlayer(encoded));
    }

    [Theory]
    [InlineData("0200000000000000000000000000000000000000000000000002000001090000000C00000000")]
    [InlineData("010000084100008242000050C0000000000000B4420000B44207010000D209")]
    [InlineData("010000084100008242000050C0000000000000B4420000B44200010000D20900")]
    [InlineData("010000084100008242000050C0000000000000B4420000B44200010000")]
    [InlineData("010000084100008242000050C0000000000000B4420000B44200010000D2")]
    [InlineData("80000000842A00008242000050C0000000000000B4420000B44200010000D209")]
    public void MovePlayerRejectsUnsupportedOrMalformedPayloads(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeMovePlayer(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void MovePlayerDecodesLiveTeleportBytes()
    {
        byte[] payload = Convert.FromHexString("0100000841723DB7420000084100000000000000000000000002000001030000000100000000");

        BedrockMovePlayerPacket decoded = BedrockActorCodecs.DecodeMovePlayer(payload);

        Assert.Equal(1UL, decoded.RuntimeEntityId);
        Assert.Equal(8.5f, decoded.Position.X);
        Assert.Equal(91.62001f, decoded.Position.Y, 0.00005f);
        Assert.Equal(8.5f, decoded.Position.Z);
        Assert.Equal(0f, decoded.Pitch);
        Assert.Equal(0f, decoded.Yaw);
        Assert.Equal(0f, decoded.HeadYaw);
        Assert.Equal(BedrockMovePlayerMode.Teleport, decoded.Mode);
        Assert.False(decoded.OnGround);
        Assert.Equal(0UL, decoded.RiddenRuntimeEntityId);
        Assert.Equal(BedrockTeleportCause.Command, decoded.TeleportCause);
        Assert.Equal(1, decoded.TeleportSourceEntityType);
        Assert.Equal(0UL, decoded.Tick);
        Assert.Equal(payload, BedrockActorCodecs.Encode(decoded));
    }

    [Fact]
    public void SetEntityMotionDecodesLiveBytes()
    {
        byte[] payload = Convert.FromHexString("0343F8083E2E90A0BD3121093E00");

        BedrockSetEntityMotionPacket decoded = BedrockActorCodecs.DecodeSetEntityMotion(payload);

        Assert.Equal(3UL, decoded.RuntimeEntityId);
        Assert.Equal(0.13375954f, decoded.Velocity.X, 0.000001f);
        Assert.Equal(-0.0784f, decoded.Velocity.Y, 0.000001f);
        Assert.Equal(0.13391568f, decoded.Velocity.Z, 0.000001f);
        Assert.Equal(0UL, decoded.Tick);
        Assert.Equal(payload, BedrockActorCodecs.Encode(decoded));
    }

    [Fact]
    public void MovePlayerRejectsNonFinitePosition()
    {
        byte[] payload = Convert.FromHexString("01").Concat(
            Convert.FromHexString("0000C07F"),
            Convert.FromHexString("00008242000050C0000000000000B4420000B44200010000D209")).ToArray();

        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeMovePlayer(payload));
    }

    [Fact]
    public void PlayerLocationCoordinatesUseExactWireForm()
    {
        var packet = new BedrockPlayerLocationPacket(5L, false, new BedrockVector3(1f, 2f, 3f), 0);
        byte[] expected = Convert.FromHexString("0A00000000803F0000004000004040");

        Assert.Equal(expected, BedrockActorCodecs.Encode(packet));
        Assert.Equal(packet, BedrockActorCodecs.DecodePlayerLocation(expected));
    }

    [Fact]
    public void PlayerLocationHideCarriesNoPosition()
    {
        var packet = new BedrockPlayerLocationPacket(5L, true, null, 0);
        byte[] expected = Convert.FromHexString("0A0100");

        Assert.Equal(expected, BedrockActorCodecs.Encode(packet));
        Assert.Equal(packet, BedrockActorCodecs.DecodePlayerLocation(expected));
    }

    [Theory]
    [InlineData("0A0200")]
    [InlineData("0A0000")]
    [InlineData("0A01000000803F0000004000004040")]
    [InlineData("0A")]
    public void PlayerLocationRejectsUnsupportedOrMalformedPayloads(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodePlayerLocation(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void AddEntityParsesTypedSections()
    {
        var packet = new BedrockAddEntityPacket(
            10L,
            3UL,
            "minecraft:pig",
            new BedrockVector3(0f, 0f, 0f),
            new BedrockVector3(0f, 0f, 0f),
            0f,
            0f,
            0f,
            0f,
            [],
            [new BedrockMetadataEntry(0U, 7U, 0, BedrockMetadataValue.FromLong(0L))],
            BedrockEntityProperties.Empty,
            []);
        byte[] expected = Convert.FromHexString("14030D6D696E6563726166743A706967")
            .Concat(new byte[40], Convert.FromHexString("000100070000000000")).ToArray();

        Assert.Equal(expected, BedrockActorCodecs.Encode(packet));
        BedrockAddEntityPacket decoded = BedrockActorCodecs.DecodeAddEntity(expected);

        Assert.Equal(packet, decoded);
        Assert.Equal("minecraft:pig", decoded.EntityType);
        BedrockMetadataEntry flags = Assert.Single(decoded.Metadata);
        Assert.Equal(0U, flags.Key);
        Assert.Equal(0L, flags.Value.Signed);
    }

    [Fact]
    public void AddEntityRejectsUnknownMetadataValueType()
    {
        byte[] payload = Convert.FromHexString("14030D6D696E6563726166743A706967")
            .Concat(new byte[40], Convert.FromHexString("0001040900")).ToArray();

        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeAddEntity(payload));
    }

    [Fact]
    public void AddPlayerValidatesSectionsAndExposesUniqueId()
    {
        var uuid = Guid.Parse("12345678-1234-1234-1234-1234567890ab");
        var packet = new BedrockAddPlayerPacket(
            uuid,
            "Bot",
            2UL,
            string.Empty,
            new BedrockVector3(0f, 0f, 0f),
            new BedrockVector3(0f, 0f, 0f),
            0f,
            0f,
            0f,
            new BedrockItemInstance(0, 0, 0U, null, 0U, []),
            0,
            [],
            BedrockEntityProperties.Empty,
            123456789L,
            1,
            0,
            [],
            [],
            "Linux",
            null);
        byte[] sections = Convert.FromHexString("0000000000000000")
            .Concat(
                Convert.FromHexString("00"),
                Convert.FromHexString("00"),
                Convert.FromHexString("0000"),
                Convert.FromHexString("15CD5B0700000000"),
                Convert.FromHexString("0100"),
                Convert.FromHexString("00"),
                Convert.FromHexString("00"),
                Convert.FromHexString("054C696E7578")).ToArray();
        byte[] expected = Convert.FromHexString("3412341278563412AB90785634123412")
            .Concat(Convert.FromHexString("03426F74"), Convert.FromHexString("0200"), new byte[36], sections).ToArray();

        Assert.Equal(expected, BedrockActorCodecs.Encode(packet));
        BedrockAddPlayerPacket decoded = BedrockActorCodecs.DecodeAddPlayer(expected);

        Assert.Equal(packet, decoded);
        Assert.Equal(uuid, decoded.Uuid);
        Assert.Equal("Bot", decoded.Username);
        Assert.Equal(0, decoded.GameType);
        Assert.Equal(123456789L, decoded.UniqueEntityId);
        Assert.Equal(1, decoded.PermissionLevel);
        Assert.Equal("Linux", decoded.DeviceId);
        Assert.Null(decoded.DeviceOs);
    }

    [Fact]
    public void AddPlayerParsesCompoundMetadataBeforeUniqueId()
    {
        byte[] sections = Convert.FromHexString("0000000000000000")
            .Concat(
                Convert.FromHexString("00"),
                Convert.FromHexString("0104050000"),
                Convert.FromHexString("0000"),
                Convert.FromHexString("0100000000000000"),
                Convert.FromHexString("0000"),
                Convert.FromHexString("00"),
                Convert.FromHexString("00"),
                Convert.FromHexString("00")).ToArray();
        byte[] payload = Convert.FromHexString("3412341278563412AB90785634123412")
            .Concat(Convert.FromHexString("03426F74"), Convert.FromHexString("0200"), new byte[36], sections).ToArray();

        BedrockAddPlayerPacket decoded = BedrockActorCodecs.DecodeAddPlayer(payload);

        Assert.Equal(1L, decoded.UniqueEntityId);
        BedrockMetadataEntry compound = Assert.Single(decoded.Metadata);
        Assert.Equal(4U, compound.Key);
        Assert.Equal(BedrockMetadataValueKind.Compound, compound.Value.Kind);
        Assert.Equal(payload, BedrockActorCodecs.Encode(decoded));
    }

    [Fact]
    public void AddPlayerDecodesExactLiveBoundaryBytes()
    {
        byte[] payload = Convert.FromHexString("3742D6B9BD5E15EE58E22BB0E6AFEDB409556D706B4C6976654101000000C0BF00009E420000F0C000000000000000000000000000000000000000000000000000000000000000000A500007078080C0808080800301020202020202000300000004040409556D706B4C6976654105070701060707000701012C010A000000160000001A0000001B0202021C0606000000250707002603030000803F270000002A01012C012B0202002C0000002D0202002E0202003503039A99193F3603036666E63F37020201380808000000000000000000000000390000003A0303000000003B0000003C030300000000460000004704040048040400490000014A0000004B0202004C0202004D0202004E020201510000015200000054040400580707015B0202005C0707808080A080801064040400650202006602020067020200680202006A0202066B0000016C0303000000416D0303000080416E040407616D6269656E746F03030000803F71000000720202007302020074020200750202007605050A000077000000780303000000007C03030000803F7D0202287E0202CC097F060600000080010202008101000000820108089A99193F6666E63F00000000830107070084010404008501080800000000000000000000000086010303000000008701030300000000880102020189010202018A010202018B010707008C01030300008042000001000000FFFFFFFF01000205003F010000000100000000000000000000000000000100FFFF0F003F000000CDCC4C3D0000803FCDCCCC3D002465653135356562642D623964362D343233372D623465642D6166653662303262653235380F000000");

        BedrockAddPlayerPacket decoded = BedrockActorCodecs.DecodeAddPlayer(payload);

        Assert.Equal("UmpkLiveA", decoded.Username);
        Assert.Equal(1UL, decoded.RuntimeEntityId);
        Assert.Equal(new BedrockVector3(-1.5f, 79f, -7.5f), decoded.Position);
        Assert.Equal(5, decoded.GameType);
        Assert.Equal(-4294967295L, decoded.UniqueEntityId);
        Assert.Equal(payload, BedrockActorCodecs.Encode(decoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("14030D6D696E6563726166743A706967")]
    [InlineData("14030D6D696E6563726166743A7069670000000000000000000000000000000000000000000000000000000000000000000000000000000001")]
    public void AddEntityRejectsMissingIdentityOrTruncatedSections(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeAddEntity(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void SetEntityDataParsesTypedSectionsAndExposesTick()
    {
        var packet = new BedrockSetEntityDataPacket(
            9UL,
            [new BedrockMetadataEntry(4U, 4U, 0, BedrockMetadataValue.FromString("Hi"))],
            BedrockEntityProperties.Empty,
            77UL);
        byte[] expected = Convert.FromHexString("090104040002486900004D");

        Assert.Equal(expected, BedrockActorCodecs.Encode(packet));
        BedrockSetEntityDataPacket decoded = BedrockActorCodecs.DecodeSetEntityData(expected);

        Assert.Equal(packet, decoded);
        Assert.Equal("Hi", Assert.Single(decoded.Metadata).Value.Text);
        Assert.Equal(77UL, decoded.Tick);
    }

    [Theory]
    [InlineData("")]
    [InlineData("09")]
    [InlineData("0904040002486900004D00")]
    [InlineData("090104090000004D")]
    public void SetEntityDataRejectsMissingOrMalformedSections(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeSetEntityData(Convert.FromHexString(payloadHex)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("02")]
    [InlineData("020000803F00000040000040C00700")]
    [InlineData("020000803F00000040000040C0")]
    [InlineData("020000C07F00000040000040C007")]
    public void SetEntityMotionRejectsMalformedPayloads(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockActorCodecs.DecodeSetEntityMotion(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void MobEffectUsesExactWireForm()
    {
        var packet = new BedrockMobEffectPacket(2UL, BedrockMobEffectEvent.Add, 10, 1, true, 600, 42UL, false);
        byte[] expected = Convert.FromHexString("0201140201B0092A00");

        Assert.Equal(expected, BedrockObservationCodecs.Encode(packet));
        BedrockMobEffectPacket decoded = BedrockObservationCodecs.DecodeMobEffect(expected);

        Assert.Equal(packet, decoded);
        Assert.Equal(10, decoded.EffectId);
        Assert.Equal(600, decoded.Duration);
        Assert.Equal(42UL, decoded.Tick);
    }

    [Fact]
    public void MobEffectPreservesRuntimeIdsBeyondInt32()
    {
        var packet = new BedrockMobEffectPacket(0x1_0000_0000UL, BedrockMobEffectEvent.Remove, 1, 0, false, -1, 0UL, true);
        byte[] encoded = BedrockObservationCodecs.Encode(packet);

        Assert.Equal(Convert.FromHexString("8080808010"), encoded[..5]);
        Assert.Equal(packet, BedrockObservationCodecs.DecodeMobEffect(encoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("02")]
    [InlineData("0200140201B0092A00")]
    [InlineData("0204140201B0092A00")]
    [InlineData("0201140201B0092A")]
    [InlineData("0201140201B0092A0000")]
    [InlineData("0201140202B0092A00")]
    public void MobEffectRejectsMalformedPayloads(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockObservationCodecs.DecodeMobEffect(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void UpdateAttributesUsesExactWireForm()
    {
        var packet = new BedrockUpdateAttributesPacket(
            3UL,
            [new BedrockPlayerAttribute(0f, 20f, 20f, 0f, 20f, 20f, "minecraft:health", [])],
            7UL);
        byte[] expected = Convert.FromHexString("0301000000000000A0410000A041000000000000A0410000A041106D696E6563726166743A6865616C74680007");

        Assert.Equal(expected, BedrockObservationCodecs.Encode(packet));
        BedrockUpdateAttributesPacket decoded = BedrockObservationCodecs.DecodeUpdateAttributes(expected);

        Assert.Equal(packet, decoded);
        BedrockPlayerAttribute attribute = Assert.Single(decoded.Attributes);
        Assert.Equal("minecraft:health", attribute.Name);
        Assert.Equal(20f, attribute.Current);
        Assert.Equal(7UL, decoded.Tick);
    }

    [Fact]
    public void UpdateAttributesRoundTripsModifiers()
    {
        var packet = new BedrockUpdateAttributesPacket(
            3UL,
            [new BedrockPlayerAttribute(
                0f, 1f, 0.5f, 0f, 1f, 0f, "minecraft:movement_speed",
                [new BedrockAttributeModifier("speed-boost", "Effect", 0.2f, 0, 2, true)])],
            9UL);
        byte[] encoded = BedrockObservationCodecs.Encode(packet);

        Assert.Equal(packet, BedrockObservationCodecs.DecodeUpdateAttributes(encoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("03")]
    [InlineData("0300")]
    [InlineData("0301000000000000A0410000A041000000000000A0410000A041106D696E6563726166743A6865616C746800")]
    [InlineData("0301000000000000A0410000A041000000000000A0410000A041106D696E6563726166743A6865616C7468000700")]
    [InlineData("0301000000000000A0410000A041000000000000A0410000A0410000000007")]
    [InlineData("03010000C07F0000A0410000A041000000000000A0410000A041106D696E6563726166743A6865616C74680007")]
    public void UpdateAttributesRejectsMalformedPayloads(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockObservationCodecs.DecodeUpdateAttributes(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void MobEquipmentUsesExactWireForm()
    {
        var packet = new BedrockMobEquipmentPacket(
            2UL,
            new BedrockItemInstance(7, 1, 0U, null, 0U, []),
            0,
            0,
            0);
        byte[] expected = Convert.FromHexString("020700010000000000000000");

        Assert.Equal(expected, BedrockObservationCodecs.Encode(packet));
        BedrockMobEquipmentPacket decoded = BedrockObservationCodecs.DecodeMobEquipment(expected);

        Assert.Equal(packet, decoded);
        Assert.Equal(7, decoded.Item.NetworkId);
        Assert.Equal(1, decoded.Item.Count);
        Assert.Null(decoded.Item.StackNetworkId);
    }

    [Fact]
    public void MobEquipmentRoundTripsStackIdAndExtraData()
    {
        var packet = new BedrockMobEquipmentPacket(
            2UL,
            new BedrockItemInstance(7, 1, 0U, 5, 0U, [0x01, 0x02]),
            1,
            2,
            0);
        byte[] encoded = BedrockObservationCodecs.Encode(packet);

        Assert.Equal(Convert.FromHexString("020700010000010A00020102010200"), encoded);
        Assert.Equal(packet, BedrockObservationCodecs.DecodeMobEquipment(encoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("02")]
    [InlineData("02070001000000000000")]
    [InlineData("02070001000000000000000000")]
    [InlineData("020700010000010A00050102010200")]
    public void MobEquipmentRejectsMalformedPayloads(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockObservationCodecs.DecodeMobEquipment(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void MobArmorEquipmentUsesExactWireForm()
    {
        var empty = new BedrockItemInstance(0, 0, 0U, null, 0U, []);
        var packet = new BedrockMobArmorEquipmentPacket(3UL, empty, empty, empty, empty, empty);
        byte[] expected = Convert.FromHexString("03").Concat(
            Convert.FromHexString("0000000000000000"),
            Convert.FromHexString("0000000000000000"),
            Convert.FromHexString("0000000000000000"),
            Convert.FromHexString("0000000000000000"),
            Convert.FromHexString("0000000000000000")).ToArray();

        Assert.Equal(expected, BedrockObservationCodecs.Encode(packet));
        BedrockMobArmorEquipmentPacket decoded = BedrockObservationCodecs.DecodeMobArmorEquipment(expected);

        Assert.Equal(packet, decoded);
        Assert.Equal(3UL, decoded.RuntimeEntityId);
        Assert.Equal(empty, decoded.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("03")]
    [InlineData("0300000000000000000000000000000000000000000000000000000000000000000000")]
    public void MobArmorEquipmentRejectsMalformedPayloads(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockObservationCodecs.DecodeMobArmorEquipment(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void SetEntityLinkUsesExactWireForm()
    {
        var packet = new BedrockSetEntityLinkPacket(new BedrockEntityLink(100L, 200L, BedrockEntityLinkType.Rider, true, true, 0.5f));
        byte[] expected = Convert.FromHexString("C80190030101010000003F");

        Assert.Equal(expected, BedrockObservationCodecs.Encode(packet));
        BedrockSetEntityLinkPacket decoded = BedrockObservationCodecs.DecodeSetEntityLink(expected);

        Assert.Equal(packet, decoded);
        Assert.Equal(100L, decoded.Link.RiddenUniqueId);
        Assert.Equal(200L, decoded.Link.RiderUniqueId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C801")]
    [InlineData("C80190030301010000003F")]
    [InlineData("C80190030101010000003F00")]
    public void SetEntityLinkRejectsMalformedPayloads(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockObservationCodecs.DecodeSetEntityLink(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void MobEffectDecodesLiveSpeedBytes()
    {
        byte[] payload = Convert.FromHexString("0201020200C0250000");

        BedrockMobEffectPacket decoded = BedrockObservationCodecs.DecodeMobEffect(payload);

        Assert.Equal(2UL, decoded.RuntimeEntityId);
        Assert.Equal(BedrockMobEffectEvent.Add, decoded.Event);
        Assert.Equal(1, decoded.EffectId);
        Assert.Equal(1, decoded.Amplifier);
        Assert.False(decoded.Particles);
        Assert.Equal(2400, decoded.Duration);
        Assert.Equal(0UL, decoded.Tick);
        Assert.False(decoded.Ambient);
        Assert.Equal(payload, BedrockObservationCodecs.Encode(decoded));
    }

    [Fact]
    public void UpdateAttributesDecodesLiveMovementModifierBytes()
    {
        byte[] payload = Convert.FromHexString("020100000000FFFF7F7F295C0F3E00000000FFFF7F7FCDCCCC3D126D696E6563726166743A6D6F76656D656E74012439316165616135362D333736622D343439382D393335622D3266376636383037303633350D4D6F76656D656E745370656564CDCCCC3E02000000020000000000");

        BedrockUpdateAttributesPacket decoded = BedrockObservationCodecs.DecodeUpdateAttributes(payload);

        Assert.Equal(2UL, decoded.RuntimeEntityId);
        BedrockPlayerAttribute attribute = Assert.Single(decoded.Attributes);
        Assert.Equal("minecraft:movement", attribute.Name);
        Assert.Equal(0f, attribute.Min);
        Assert.Equal(float.MaxValue, attribute.Max);
        Assert.Equal(BitConverter.Int32BitsToSingle(0x3E0F5C29), attribute.Current);
        Assert.Equal(0.1f, attribute.Default);
        BedrockAttributeModifier modifier = Assert.Single(attribute.Modifiers);
        Assert.Equal("91aeaa56-376b-4498-935b-2f7f68070635", modifier.Id);
        Assert.Equal("MovementSpeed", modifier.Name);
        Assert.Equal(0.4f, modifier.Amount);
        Assert.Equal(2, modifier.Operation);
        Assert.Equal(2, modifier.Operand);
        Assert.False(modifier.Serializable);
        Assert.Equal(0UL, decoded.Tick);
        Assert.Equal(payload, BedrockObservationCodecs.Encode(decoded));
    }

    [Fact]
    public void MobEquipmentDecodesLiveZombieSwordBytes()
    {
        byte[] payload = Convert.FromHexString("07350101000000001CFFFF010A000003060044616D61676500000000000000000000000000000000");

        BedrockMobEquipmentPacket decoded = BedrockObservationCodecs.DecodeMobEquipment(payload);

        Assert.Equal(7UL, decoded.RuntimeEntityId);
        Assert.Equal(309, decoded.Item.NetworkId);
        Assert.Equal(1, decoded.Item.Count);
        Assert.Equal(0U, decoded.Item.MetadataValue);
        Assert.Null(decoded.Item.StackNetworkId);
        Assert.Equal(0U, decoded.Item.BlockRuntimeId);
        Assert.Equal(28, decoded.Item.ExtraData.Length);
        Assert.Equal(0, decoded.InventorySlot);
        Assert.Equal(0, decoded.SelectedSlot);
        Assert.Equal(0, decoded.WindowId);
        Assert.Equal(payload, BedrockObservationCodecs.Encode(decoded));
    }

    [Fact]
    public void MobArmorEquipmentDecodesLiveZombieHelmetBytes()
    {
        byte[] payload = Convert.FromHexString("075A0101000000001CFFFF010A000003060044616D616765000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");

        BedrockMobArmorEquipmentPacket decoded = BedrockObservationCodecs.DecodeMobArmorEquipment(payload);

        Assert.Equal(7UL, decoded.RuntimeEntityId);
        Assert.Equal(346, decoded.Helmet.NetworkId);
        Assert.Equal(1, decoded.Helmet.Count);
        Assert.Equal(28, decoded.Helmet.ExtraData.Length);
        Assert.Equal(0, decoded.Chestplate.NetworkId);
        Assert.Equal(0, decoded.Leggings.NetworkId);
        Assert.Equal(0, decoded.Boots.NetworkId);
        Assert.Equal(0, decoded.Body.NetworkId);
        Assert.Equal(payload, BedrockObservationCodecs.Encode(decoded));
    }

    [Fact]
    public void SetEntityLinkDecodesLiveRideBytes()
    {
        byte[] payload = Convert.FromHexString("EFFFFFFF1FFBFFFFFF1F01000100000000");

        BedrockSetEntityLinkPacket decoded = BedrockObservationCodecs.DecodeSetEntityLink(payload);

        Assert.Equal(-4294967288L, decoded.Link.RiddenUniqueId);
        Assert.Equal(-4294967294L, decoded.Link.RiderUniqueId);
        Assert.Equal(BedrockEntityLinkType.Rider, decoded.Link.Type);
        Assert.False(decoded.Link.Immediate);
        Assert.True(decoded.Link.RiderInitiated);
        Assert.Equal(0f, decoded.Link.AngularVelocity);
        Assert.Equal(payload, BedrockObservationCodecs.Encode(decoded));
    }

    [Fact]
    public void MetadataEntriesParseToTypedValues()
    {
        byte[] payload = Convert.FromHexString("0300070706040404024869C80102020A");

        ImmutableArray<BedrockMetadataEntry> entries = BedrockActorSections.ParseMetadataDictionary(payload, "test metadata");

        Assert.Equal(3, entries.Length);
        Assert.Equal(0U, entries[0].Key);
        Assert.Equal(BedrockMetadataValueKind.Long, entries[0].Value.Kind);
        Assert.Equal(3L, entries[0].Value.Signed);
        Assert.Equal("Hi", entries[1].Value.Text);
        Assert.Equal(200U, entries[2].Key);
        Assert.Equal(5, entries[2].Value.Signed);
        Assert.Equal(payload, BedrockActorSections.EncodeMetadataDictionary(entries));
    }

    [Theory]
    [InlineData("0104090900")]
    [InlineData("0104050000FF")]
    [InlineData("00FF")]
    public void MetadataEntriesRejectUnknownTypesAndTruncation(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockActorSections.ParseMetadataDictionary(Convert.FromHexString(payloadHex), "test metadata"));
    }
}

internal static class ActorTestByteExtensions
{
    internal static byte[] Concat(this byte[] first, params byte[][] rest)
    {
        int length = first.Length + rest.Sum(static part => part.Length);
        var combined = new byte[length];
        int offset = 0;
        first.CopyTo(combined, offset);
        offset += first.Length;
        foreach (byte[] part in rest)
        {
            part.CopyTo(combined, offset);
            offset += part.Length;
        }

        return combined;
    }
}
