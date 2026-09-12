using System.Buffers;
using Umpk.Game.Entities;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Tests.Item;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Hand-authored byte round-trip tests for the particle-list metadata shape and the 26.2 resolvable-profile shape. Bytes are laid out by hand from the wire contract, decoded through the structural serializer, then re-encoded and compared, so the wire shape (not a symmetric self-round-trip alone) is pinned.</summary>
public sealed class MetadataExtraCodecsTests
{
    private static PacketCodecContext Ctx => ItemTestRegistries.Context;

    private static ItemComponentTable Icons776 => ItemStackCodecs.ComponentsV26_2;

    [Fact]
    public void Particles_List_RoundTrips_FromHandAuthoredBytes()
    {
        // A list of two particles: one option-carrying (effect id 23: SpellParticleOption int+float) and one option-less (id 0). The list is count-prefixed, followed by each particle payload.
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(2);          // list count
        w.WriteVarInt(23);         // effect
        w.WriteInt(0x0055AA33);    // color
        w.WriteFloat(2.5f);        // power
        w.WriteVarInt(0);          // an option-less particle
        byte[] payload = buffer.WrittenSpan.ToArray();

        var r = new PacketReader(payload);
        MetadataValue value = MetadataExtraCodecs.ReadParticles(ref r, ParticleCodec.ModernV26_2, Icons776, Ctx);
        Assert.Equal(0, r.Remaining);
        Assert.Equal(MetadataValueKind.Particles, value.Kind);
        Assert.Equal(2, value.AsParticles().Count);

        var outBuf = new ArrayBufferWriter<byte>();
        var ow = new PacketWriter(outBuf);
        MetadataExtraCodecs.WriteParticles(ref ow, value);
        Assert.Equal(payload, outBuf.WrittenSpan.ToArray());
    }

    [Fact]
    public void ResolvableProfile_Resolved_RoundTrips_FromHandAuthoredBytes()
    {
        var id = new Guid("12345678-1234-1234-1234-1234567890ab");
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteBool(true);             // either: resolved game profile
        w.WriteUuid(id);
        w.WriteString("Notch");
        w.WriteVarInt(1);              // one property
        w.WriteString("textures");
        w.WriteString("BASE64VALUE");
        w.WriteBool(true);             // signature present
        w.WriteString("SIGVALUE");
        // Skin patch: body present, cape/elytra absent, model = slim.
        w.WriteBool(true);
        w.WriteString("minecraft:textures/body.png");
        w.WriteBool(false);
        w.WriteBool(false);
        w.WriteBool(true);             // model present
        w.WriteBool(true);             // slim
        byte[] payload = buffer.WrittenSpan.ToArray();

        var r = new PacketReader(payload);
        MetadataValue value = MetadataExtraCodecs.ReadResolvableProfile(ref r);
        Assert.Equal(0, r.Remaining);

        ResolvableProfile profile = value.AsResolvableProfile();
        Assert.True(profile.IsResolved);
        Assert.Equal(id, profile.Id);
        Assert.Equal("Notch", profile.Name);
        Assert.Single(profile.Properties);
        Assert.Equal("textures", profile.Properties[0].Name);
        Assert.Equal("SIGVALUE", profile.Properties[0].Signature);
        Assert.True(profile.Skin.SlimModel);
        Assert.NotNull(profile.Skin.Body);
        Assert.Null(profile.Skin.Cape);

        var outBuf = new ArrayBufferWriter<byte>();
        var ow = new PacketWriter(outBuf);
        MetadataExtraCodecs.WriteResolvableProfile(ref ow, value);
        Assert.Equal(payload, outBuf.WrittenSpan.ToArray());
    }

    [Fact]
    public void ResolvableProfile_Partial_RoundTrips_FromHandAuthoredBytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteBool(false);            // either: partial profile
        w.WriteBool(true);             // optional name present
        w.WriteString("Steve");
        w.WriteBool(false);            // optional id absent
        w.WriteVarInt(0);              // empty property map
        // Empty skin patch: all absent.
        w.WriteBool(false);
        w.WriteBool(false);
        w.WriteBool(false);
        w.WriteBool(false);
        byte[] payload = buffer.WrittenSpan.ToArray();

        var r = new PacketReader(payload);
        MetadataValue value = MetadataExtraCodecs.ReadResolvableProfile(ref r);
        Assert.Equal(0, r.Remaining);

        ResolvableProfile profile = value.AsResolvableProfile();
        Assert.False(profile.IsResolved);
        Assert.Equal("Steve", profile.Name);
        Assert.Null(profile.Id);
        Assert.Empty(profile.Properties);
        Assert.Null(profile.Skin.SlimModel);

        var outBuf = new ArrayBufferWriter<byte>();
        var ow = new PacketWriter(outBuf);
        MetadataExtraCodecs.WriteResolvableProfile(ref ow, value);
        Assert.Equal(payload, outBuf.WrittenSpan.ToArray());
    }
}
