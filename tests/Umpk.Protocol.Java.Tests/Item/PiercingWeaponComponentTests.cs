using System.Buffers;
using System.Text.Json;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public sealed class PiercingWeaponComponentTests
{
    private static readonly JsonDocument Native = ReadNative();

    public static IEnumerable<object[]> Cases => Native.RootElement.GetProperty("groups").EnumerateArray()
        .SelectMany(group => group.GetProperty("cases").EnumerateArray().Select((row, index) => new object[] { group.GetProperty("protocol").GetInt32(), index }));

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeSlotPatchDecodesAndRoundTripsWithoutDiscardingThePacket(int protocol, int index)
    {
        JsonElement row = Native.RootElement.GetProperty("groups").EnumerateArray().Single(group => group.GetProperty("protocol").GetInt32() == protocol).GetProperty("cases")[index];
        byte[] bytes = Convert.FromHexString(row.GetProperty("slot_hex").GetString()!);
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);

        var decoded = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.Decode(bytes, context));

        Assert.Equal(1, decoded.Item.Count);
        Assert.Equal(Identifier.Minecraft("stone"), decoded.Item.Item.Id);
        Assert.False(decoded.Item.Components.HasNoPatch);
        Assert.True(decoded.Item.Components.TryGet(DataComponents.PiercingWeapon, out PiercingWeaponComponent? piercing));
        int flags = row.GetProperty("flags").GetInt32();
        Assert.Equal((flags & 1) != 0, piercing.DealsKnockback);
        Assert.Equal((flags & 2) != 0, piercing.Dismounts);
        AssertSound(row.GetProperty("sound_kind").GetInt32(), piercing.Sound, context);
        AssertSound(row.GetProperty("hit_sound_kind").GetInt32(), piercing.HitSound, context);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(ref writer, decoded, context);
        Assert.Equal(bytes, buffer.WrittenSpan.ToArray());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ComponentHashMatchesTheNativeRegistryAwareCodec(int protocol, int index)
    {
        JsonElement row = Native.RootElement.GetProperty("groups").EnumerateArray().Single(group => group.GetProperty("protocol").GetInt32() == protocol).GetProperty("cases")[index];
        ItemComponentTable table = Table(protocol);
        int wireId = table.WireId(DataComponents.Unmodeled(Identifier.Minecraft("piercing_weapon")));
        ItemComponentCodec codec = table.CompactCodec(wireId);
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        var reader = new PacketReader(Convert.FromHexString(row.GetProperty("component_hex").GetString()!));
        object value = codec.Decode(ref reader, context);
        Assert.Equal(0, reader.Remaining);

        Assert.Equal(row.GetProperty("hash").GetInt32(), codec.Hash(new HashOps(), value, context));
        Assert.Equal(row.GetProperty("hash").GetInt32(), codec.Hash(new HashOps(forceSoftwareCrc: true), value, context));
    }

    [Fact]
    public void AReferenceWithoutRoomForTheWireMarkerIsRejected()
    {
        var value = new PiercingWeaponComponent(Sound: new SoundEventRef(int.MaxValue));
        Assert.Throws<ProtocolViolationException>(() => EncodeComponent(value, PacketCodecContext.Registryless));
    }

    [Theory]
    [InlineData(0, int.MinValue, false)]
    [InlineData(0x7FC00001, 0x7FC00002, true)]
    [InlineData(0x7FC00001, -4194302, true)]
    [InlineData(0, 0, true)]
    public void NativeSoundRangeEqualityPreservesTheComponentPatch(int leftBits, int rightBits, bool nativeEqual)
    {
        // Native 26.3 PiercingWeapon equality distinguishes signed zero and canonicalizes NaN payloads and signs.
        Identifier name = Identifier.Parse("umpk:probe");
        var left = new PiercingWeaponComponent(Sound: new SoundEventRef(null, name, BitConverter.Int32BitsToSingle(leftBits)));
        var right = new PiercingWeaponComponent(Sound: new SoundEventRef(null, name, BitConverter.Int32BitsToSingle(rightBits)));
        Assert.Equal(nativeEqual, left.Equals(right));
        if (nativeEqual)
            Assert.Equal(left.GetHashCode(), right.GetHashCode());
        var prototype = new Dictionary<DataComponentType, object> { [DataComponents.PiercingWeapon] = left };
        Assert.Equal(nativeEqual, DataComponentMap.FromPrototype(prototype).With(DataComponents.PiercingWeapon, right).HasNoPatch);
    }

    private static byte[] EncodeComponent(PiercingWeaponComponent value, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        ItemComponentCodecs.PiercingWeapon.Encode(ref writer, value, context);
        return buffer.WrittenSpan.ToArray();
    }

    private static ItemComponentTable Table(int protocol) => protocol switch
    {
        774 => ItemComponentTable.V1_21_11(),
        775 => ItemComponentTable.V26_1(),
        776 => ItemComponentTable.V26_2(),
        777 => ItemComponentTable.V26_3(),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };

    private static void AssertSound(int kind, SoundEventRef? sound, PacketCodecContext context)
    {
        if (kind == 0)
        {
            Assert.Null(sound);
            return;
        }
        Assert.NotNull(sound);
        if (kind == 1)
        {
            Assert.NotNull(sound.RegistryId);
            Assert.True(context.Registries.TryGetRegistry(Identifier.Minecraft("sound_event"), out var registry));
            Assert.True(registry.TryGetKey(sound.RegistryId.Value, out Identifier name));
            Assert.Equal(Identifier.Minecraft("entity.player.attack.sweep"), name);
            Assert.Null(sound.SoundId);
        }
        else
        {
            Assert.Null(sound.RegistryId);
            Assert.Equal(Identifier.Parse("umpk:probe"), sound.SoundId);
        }
        Assert.Equal(kind == 3 ? 12.5F : (float?)null, sound.FixedRange);
    }

    private static JsonDocument ReadNative()
    {
        using Stream source = typeof(PiercingWeaponComponentTests).Assembly.GetManifestResourceStream("Umpk.Protocol.Java.Tests.Fixtures.PiercingWeapon.native.json")
            ?? throw new InvalidOperationException("The native piercing component witness is missing.");
        return JsonDocument.Parse(source);
    }
}
