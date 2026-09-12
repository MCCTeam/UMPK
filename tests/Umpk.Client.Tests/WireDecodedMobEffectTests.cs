using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The consequence test for populating <c>minecraft:mob_effect</c>.
/// <para>Populating that registry is a LIVE behaviour change on 47 and 477-776, not just a table becoming non-empty. <c>EntityApplier.ApplyEffectAsync</c> resolves a non-self effect through the session registry and only calls <c>entity.AddOrRefreshEffect</c> when that resolves; against the empty registry it resolved to <c>null</c> every time and the decoded effect was silently dropped, so a tracked entity's <c>Effects</c> map was permanently empty in every live session. The sibling tests for the same registry work are enchantment-only and none of them touches this path.</para>
/// <para>Every case starts from bytes and decodes them through the codec the version catalog binds for <c>minecraft:update_mob_effect</c> at that protocol (<see cref="BoundDescriptorCodec"/>), then applies the decoded packet through the real applier chain, and only then reads the entity's effect map back. The entity itself is spawned through the same applier chain first, since an untracked entity is the applier's own early-return case.</para>
/// </summary>
public sealed class WireDecodedMobEffectTests
{
    private const int ComponentEraProtocol = 766; // 1.20.6, zero-based mob-effect ids.
    private const int OneBasedProtocol = 763;     // 1.20.1, the last one-based era.
    private const int ZeroBasedProtocol = 764;    // 1.20.2, the first zero-based era.

    private const int EntityId = 42;

    /// <summary>
    /// A 1.20.6 <c>update_mob_effect</c> for entity 42, effect holder 7, amplifier 1, duration 600 ticks, flags 0x06 (show particles + show icon, the value vanilla sends for a normal potion effect).
    /// <para>Bytes: <c>2A</c> VarInt entity id 42 | <c>07</c> VarInt effect holder id 7 | <c>01</c> VarInt amplifier 1 | <c>D804</c> VarInt duration 600 | <c>06</c> flags byte. That field order is <c>EntityEffectCodecs.UpdateMobEffectV1_20_5</c>, which <c>EntityBindings</c> binds from <c>V1_20_5</c> (protocol 766) on: the 1.20.5 form widened the amplifier to a VarInt and dropped the trailing nullable <c>FactorData</c> compound the 1.19-1.20.4 form carries.</para>
    /// <para>Holder 7 is <c>minecraft:jump_boost</c> on this protocol, and NOTHING on the wire says so: only the protocol's <c>minecraft:mob_effect</c> registry names it.</para>
    /// </summary>
    private const string JumpBoost2Frame766 = "2A0701D80406";

    /// <summary>The 1.19-1.20.4 <c>update_mob_effect</c> form, and the SAME six bytes are legal on both 763 and 764: <c>2A</c> entity 42 | <c>07</c> VarInt effect id 7 | <c>01</c> amplifier BYTE 1 | <c>D804</c> VarInt duration 600 | <c>06</c> flags | <c>00</c> the <c>writeNullable</c> false for <c>FactorData</c>. Both protocols bind <c>EntityEffectCodecs.UpdateMobEffectV1_19</c> (<c>EntityBindings</c> rebinds only at <c>V1_20_5</c>), so the frame is byte-identical across the boundary and only the REGISTRY differs. That is what makes the pair below a real boundary test.</summary>
    private const string Effect7Frame763And764 = "2A0701D8040600";

    /// <summary>The consequence: a wire-decoded effect for a tracked entity now lands in that entity's effect map with the right identity. Against the empty registry the applier resolved null and added nothing, so `Effects` stayed empty however many effect packets arrived.</summary>
    [Fact]
    public async Task ComponentWireLayout766_WireDecodedEntityEffect_LandsOnTheTrackedEntityWithItsIdentity()
    {
        Entity entity = await ApplyEffectFrameAsync(ComponentEraProtocol, JumpBoost2Frame766);

        EffectInstance effect = Assert.Single(entity.Effects).Value;
        Assert.False(effect.Effect.IsDefault);
        Assert.Equal(Identifier.Minecraft("jump_boost"), effect.Effect.Id);
        Assert.Equal(7, effect.Effect.NetworkId);
        Assert.Equal(1, effect.Amplifier);
        Assert.Equal(2, effect.Level);
        Assert.Equal(600, effect.Duration);
        Assert.True(effect.ShowParticles);
        Assert.True(effect.ShowIcon);
        Assert.False(effect.IsAmbient);
    }

    /// <summary>
    /// The 1-based to 0-based boundary, end to end. The dataset records <c>minecraft:mob_effect</c> shifting from 1-based to 0-based ids at protocol 764 (1.20.2), and <c>EnchantmentAndMobEffectRegistryTests</c> pins that at the table level. This is the same fact one layer up: the IDENTICAL six-byte frame, through the IDENTICAL codec (<c>UpdateMobEffectV1_19</c> on both), resolves to a DIFFERENT effect either side of the boundary - instant damage on 1.20.1, jump boost on 1.20.2. A client that used one era's table on the other would silently mis-name every status effect, and only a test that crosses the boundary can see it.
    /// <para><c>EnchantmentAndMobEffectRegistryTests</c> independently verifies this registry-ID boundary.</para>
    /// </summary>
    [Theory]
    [InlineData(OneBasedProtocol, "instant_damage")]
    [InlineData(ZeroBasedProtocol, "jump_boost")]
    public async Task TheSameEffectFrame_ResolvesDifferently_AcrossTheOneToZeroBasedBoundary(
        int protocol, string expectedName)
    {
        Entity entity = await ApplyEffectFrameAsync(protocol, Effect7Frame763And764);

        EffectInstance effect = Assert.Single(entity.Effects).Value;
        Assert.False(effect.Effect.IsDefault);
        Assert.Equal(Identifier.Minecraft(expectedName), effect.Effect.Id);
        Assert.Equal(7, effect.Effect.NetworkId);
        Assert.Equal(600, effect.Duration);
    }

    /// <summary>Spawns entity 42 through the real applier chain, decodes the given <c>update_mob_effect</c> frame through the protocol's bound codec, applies it, and returns the tracked entity.</summary>
    private static async Task<Entity> ApplyEffectFrameAsync(int protocol, string frameHex)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);

        // The session's registries are the production ones for this protocol - the same object UmpkClient installs from JavaGameData.Registries(protocol).
        harness.State.Registries = JavaGameData.Registries(protocol);
        harness.State.Self.EntityId = 100; // anything but EntityId, so this is NOT the self branch.

        await harness.ApplyAsync(new ClientboundAddEntityPacket(
            EntityId, Guid.NewGuid(), 0, 0, 64, 0, 0, 0, 0, 0, 0, 0, 0, null));

        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "update_mob_effect");
        var context = new PacketCodecContext(harness.State.Registries, IConnectionCodecState.Empty);
        var decoded = (ClientboundUpdateMobEffectPacket)codec.Decode(Convert.FromHexString(frameHex), context);

        // The decode itself, pinned separately so the assertions above cannot pass for the wrong reason.
        Assert.Equal(EntityId, decoded.EntityId);
        Assert.Equal(7, decoded.EffectId);

        await harness.ApplyAsync(decoded);

        Assert.True(harness.State.Entities.TryGet(EntityId, out Entity? entity));
        Assert.NotNull(entity);
        return entity!;
    }
}
