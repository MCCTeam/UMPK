using System.Buffers;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public sealed class LegacyCustomModelIntegerCodecTests
{
    // Original full-packet outputs, scalar DATA and observed float projection bits from all three legacy representatives.
    public static IEnumerable<object[]> NativeCases()
    {
        yield return [766, "same-zero", "AAcAJAHHBgEADQA=", "AAcAJAHHBgEADQA=", 0, 0, false, 0, 0];
        yield return [766, "same-small-positive", "AAcAJAHHBgEADSo=", "AAcAJAHHBgEADSo=", 42, 42, false, 1109917696, 1109917696];
        yield return [766, "same-small-negative", "AAcAJAHHBgEADdb///8P", "AAcAJAHHBgEADdb///8P", -42, -42, false, -1037565952, -1037565952];
        yield return [766, "same-largest-exact", "AAcAJAHHBgEADf///wc=", "AAcAJAHHBgEADf///wc=", 16777215, 16777215, false, 1266679807, 1266679807];
        yield return [766, "same-first-rounded", "AAcAJAHHBgEADYGAgAg=", "AAcAJAHHBgEADYGAgAg=", 16777217, 16777217, false, 1266679808, 1266679808];
        yield return [766, "rounded-neighbors-positive", "AAcAJAHHBgEADYCAgAg=", "AAcAJAHHBgEADYGAgAg=", 16777216, 16777217, false, 1266679808, 1266679808];
        yield return [766, "rounded-neighbors-negative", "AAcAJAHHBgEADYCAgPgP", "AAcAJAHHBgEADf////cP", -16777216, -16777217, false, -880803840, -880803840];
        yield return [766, "same-max", "AAcAJAHHBgEADf////8H", "AAcAJAHHBgEADf////8H", 2147483647, 2147483647, false, 1325400064, 1325400064];
        yield return [766, "max-neighbors", "AAcAJAHHBgEADf7///8H", "AAcAJAHHBgEADf////8H", 2147483646, 2147483647, false, 1325400064, 1325400064];
        yield return [766, "same-min", "AAcAJAHHBgEADYCAgIAI", "AAcAJAHHBgEADYCAgIAI", -2147483648, -2147483648, false, -822083584, -822083584];
        yield return [766, "min-neighbors", "AAcAJAHHBgEADYCAgIAI", "AAcAJAHHBgEADYGAgIAI", -2147483648, -2147483647, false, -822083584, -822083584];
        yield return [766, "same-boundary-exact", "AAcAJAHHBgEADYCAgAg=", "AAcAJAHHBgEADYCAgAg=", 16777216, 16777216, false, 1266679808, 1266679808];
        yield return [766, "zero-versus-absent", "AAcAJAHHBgEADQA=", "AAcAJAHHBgAA", 0, 0, true, 0, 0];
        yield return [767, "same-zero", "AAcAJAHIBgEADQA=", "AAcAJAHIBgEADQA=", 0, 0, false, 0, 0];
        yield return [767, "same-small-positive", "AAcAJAHIBgEADSo=", "AAcAJAHIBgEADSo=", 42, 42, false, 1109917696, 1109917696];
        yield return [767, "same-small-negative", "AAcAJAHIBgEADdb///8P", "AAcAJAHIBgEADdb///8P", -42, -42, false, -1037565952, -1037565952];
        yield return [767, "same-largest-exact", "AAcAJAHIBgEADf///wc=", "AAcAJAHIBgEADf///wc=", 16777215, 16777215, false, 1266679807, 1266679807];
        yield return [767, "same-first-rounded", "AAcAJAHIBgEADYGAgAg=", "AAcAJAHIBgEADYGAgAg=", 16777217, 16777217, false, 1266679808, 1266679808];
        yield return [767, "rounded-neighbors-positive", "AAcAJAHIBgEADYCAgAg=", "AAcAJAHIBgEADYGAgAg=", 16777216, 16777217, false, 1266679808, 1266679808];
        yield return [767, "rounded-neighbors-negative", "AAcAJAHIBgEADYCAgPgP", "AAcAJAHIBgEADf////cP", -16777216, -16777217, false, -880803840, -880803840];
        yield return [767, "same-max", "AAcAJAHIBgEADf////8H", "AAcAJAHIBgEADf////8H", 2147483647, 2147483647, false, 1325400064, 1325400064];
        yield return [767, "max-neighbors", "AAcAJAHIBgEADf7///8H", "AAcAJAHIBgEADf////8H", 2147483646, 2147483647, false, 1325400064, 1325400064];
        yield return [767, "same-min", "AAcAJAHIBgEADYCAgIAI", "AAcAJAHIBgEADYCAgIAI", -2147483648, -2147483648, false, -822083584, -822083584];
        yield return [767, "min-neighbors", "AAcAJAHIBgEADYCAgIAI", "AAcAJAHIBgEADYGAgIAI", -2147483648, -2147483647, false, -822083584, -822083584];
        yield return [767, "same-boundary-exact", "AAcAJAHIBgEADYCAgAg=", "AAcAJAHIBgEADYCAgAg=", 16777216, 16777216, false, 1266679808, 1266679808];
        yield return [767, "zero-versus-absent", "AAcAJAHIBgEADQA=", "AAcAJAHIBgAA", 0, 0, true, 0, 0];
        yield return [768, "same-zero", "AAcAJAHeBgEADgA=", "AAcAJAHeBgEADgA=", 0, 0, false, 0, 0];
        yield return [768, "same-small-positive", "AAcAJAHeBgEADio=", "AAcAJAHeBgEADio=", 42, 42, false, 1109917696, 1109917696];
        yield return [768, "same-small-negative", "AAcAJAHeBgEADtb///8P", "AAcAJAHeBgEADtb///8P", -42, -42, false, -1037565952, -1037565952];
        yield return [768, "same-largest-exact", "AAcAJAHeBgEADv///wc=", "AAcAJAHeBgEADv///wc=", 16777215, 16777215, false, 1266679807, 1266679807];
        yield return [768, "same-first-rounded", "AAcAJAHeBgEADoGAgAg=", "AAcAJAHeBgEADoGAgAg=", 16777217, 16777217, false, 1266679808, 1266679808];
        yield return [768, "rounded-neighbors-positive", "AAcAJAHeBgEADoCAgAg=", "AAcAJAHeBgEADoGAgAg=", 16777216, 16777217, false, 1266679808, 1266679808];
        yield return [768, "rounded-neighbors-negative", "AAcAJAHeBgEADoCAgPgP", "AAcAJAHeBgEADv////cP", -16777216, -16777217, false, -880803840, -880803840];
        yield return [768, "same-max", "AAcAJAHeBgEADv////8H", "AAcAJAHeBgEADv////8H", 2147483647, 2147483647, false, 1325400064, 1325400064];
        yield return [768, "max-neighbors", "AAcAJAHeBgEADv7///8H", "AAcAJAHeBgEADv////8H", 2147483646, 2147483647, false, 1325400064, 1325400064];
        yield return [768, "same-min", "AAcAJAHeBgEADoCAgIAI", "AAcAJAHeBgEADoCAgIAI", -2147483648, -2147483648, false, -822083584, -822083584];
        yield return [768, "min-neighbors", "AAcAJAHeBgEADoCAgIAI", "AAcAJAHeBgEADoGAgIAI", -2147483648, -2147483647, false, -822083584, -822083584];
        yield return [768, "same-boundary-exact", "AAcAJAHeBgEADoCAgAg=", "AAcAJAHeBgEADoCAgAg=", 16777216, 16777216, false, 1266679808, 1266679808];
        yield return [768, "zero-versus-absent", "AAcAJAHeBgEADgA=", "AAcAJAHeBgAA", 0, 0, true, 0, 0];
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void NativeIntegerSurvivesBoundSlotRoundTripAndDataHash(int protocol, string scenario, string before, string after, int beforeInteger, int afterInteger, bool afterAbsent, int beforeProjectionBits, int afterProjectionBits)
    {
        Assert.NotEmpty(scenario);
        Check(before, beforeInteger, beforeProjectionBits, absent: false);
        Check(after, afterInteger, afterProjectionBits, afterAbsent);

        void Check(string payload, int integer, int projectionBits, bool absent)
        {
            byte[] original = Convert.FromBase64String(payload);
            ClientboundContainerSetSlotPacket packet = Decode(protocol, original);
            Assert.Equal(0, packet.ContainerId);
            Assert.Equal(7, packet.StateId);
            Assert.Equal(36, packet.Slot);
            Assert.Equal(Identifier.Minecraft("diamond_pickaxe"), packet.Item.Item.Id);
            Assert.Equal(1, packet.Item.Count);
            CustomModelDataComponent? data = packet.Item.Components.Get(DataComponents.CustomModelData);
            if (absent)
                Assert.Null(data);
            else
            {
                Assert.NotNull(data);
                Assert.Equal(projectionBits, BitConverter.SingleToInt32Bits(Assert.Single(data.Floats)));
                Assert.Empty(data.Flags);
                Assert.Empty(data.Strings);
                Assert.Empty(data.Colors);
            }
            Assert.Equal(original, Encode(protocol, packet));
            if (data is not null)
                Assert.Equal(new HashOps().Int(integer), ItemComponentCodecs.CustomModelDataV1_20_5.Hash(new HashOps(), data, Context(protocol)));
        }
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    public void CopyingARecordThatRetainsItsProjectionRetainsTheOriginalInteger(int protocol)
    {
        byte[] original = Payload(protocol, "same-first-rounded");
        ClientboundContainerSetSlotPacket packet = Decode(protocol, original);
        var received = Assert.IsType<CustomModelDataComponent>(packet.Item.Components.Get(DataComponents.CustomModelData));
        CustomModelDataComponent copy = received with { };
        Assert.NotSame(received, copy);
        Assert.Same(received.Floats, copy.Floats);
        var changed = packet with { Item = packet.Item.With(DataComponents.CustomModelData, copy) };
        Assert.Equal(original, Encode(protocol, changed));
        Assert.Equal(new HashOps().Int(16_777_217), ItemComponentCodecs.CustomModelDataV1_20_5.Hash(new HashOps(), copy, Context(protocol)));
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    public void AChangedSmallProjectionCannotReplayThePreviousLargeInteger(int protocol)
    {
        ClientboundContainerSetSlotPacket packet = Decode(protocol, Payload(protocol, "same-first-rounded"));
        var received = Assert.IsType<CustomModelDataComponent>(packet.Item.Components.Get(DataComponents.CustomModelData));
        CustomModelDataComponent edited = ChangeProjection(received, 42);
        var changed = packet with { Item = packet.Item.With(DataComponents.CustomModelData, edited) };
        Assert.Equal(Payload(protocol, "same-small-positive"), Encode(protocol, changed));
        Assert.Equal(new HashOps().Int(42), ItemComponentCodecs.CustomModelDataV1_20_5.Hash(new HashOps(), edited, Context(protocol)));
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    public void AChangedAmbiguousProjectionDoesNotReuseAnOriginalDataHash(int protocol)
    {
        ClientboundContainerSetSlotPacket packet = Decode(protocol, Payload(protocol, "same-first-rounded"));
        var received = Assert.IsType<CustomModelDataComponent>(packet.Item.Components.Get(DataComponents.CustomModelData));
        CustomModelDataComponent edited = ChangeProjection(received, -16_777_216);
        var changed = packet with { Item = packet.Item.With(DataComponents.CustomModelData, edited) };
        // Encoding a changed public model keeps its existing projection contract; its lost original integer is not a known DATA value.
        Assert.Equal(Payload(protocol, "rounded-neighbors-negative"), Encode(protocol, changed));
        Assert.Throws<ComponentHashUnavailableException>(() => ItemComponentCodecs.CustomModelDataV1_20_5.Hash(new HashOps(), edited, Context(protocol)));
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    public void ANewProjectionListDoesNotInventTheLostOriginalInteger(int protocol)
    {
        byte[] original = Payload(protocol, "same-first-rounded");
        ClientboundContainerSetSlotPacket packet = Decode(protocol, original);
        var received = Assert.IsType<CustomModelDataComponent>(packet.Item.Components.Get(DataComponents.CustomModelData));
        CustomModelDataComponent copy = received with { Floats = received.Floats.ToArray() };
        Assert.NotSame(received.Floats, copy.Floats);
        var changed = packet with { Item = packet.Item.With(DataComponents.CustomModelData, copy) };
        Assert.Equal(Payload(protocol, "rounded-neighbors-positive"), Encode(protocol, changed));
        Assert.Throws<ComponentHashUnavailableException>(() => ItemComponentCodecs.CustomModelDataV1_20_5.Hash(new HashOps(), copy, Context(protocol)));
        Assert.Equal(original, Encode(protocol, packet));
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    public void LegacyProvenanceCannotReplaceTheAdjacentModernFourListShape(int protocol)
    {
        ClientboundContainerSetSlotPacket legacy = Decode(protocol, Payload(protocol, "same-first-rounded"));
        var received = Assert.IsType<CustomModelDataComponent>(legacy.Item.Components.Get(DataComponents.CustomModelData));
        CustomModelDataComponent projectedZero = ChangeProjection(received, 0);
        byte[] modernOriginal = Convert.FromBase64String(ModernPositiveZeroPayload);
        ClientboundContainerSetSlotPacket modern = Decode(769, modernOriginal);
        var modernData = Assert.IsType<CustomModelDataComponent>(modern.Item.Components.Get(DataComponents.CustomModelData));
        CustomModelDataComponent foreignProjection = modernData with { Floats = projectedZero.Floats };
        var changed = modern with { Item = modern.Item.With(DataComponents.CustomModelData, foreignProjection) };
        Assert.Equal(modernOriginal, Encode(769, changed));
        var ops = new HashOps();
        int expected = ops.Map([
            (ops.String("floats"), ops.List([ops.Float(0.0F)])),
            (ops.String("flags"), ops.List([ops.Boolean(true), ops.Boolean(false)])),
            (ops.String("strings"), ops.List([ops.String("alpha"), ops.String("beta")])),
            (ops.String("colors"), ops.List([ops.Int(0x112233), ops.Int(0x445566)])),
        ]);
        Assert.Equal(expected, ItemComponentCodecs.CustomModelData.Hash(ops, foreignProjection, Context(769)));
    }

    private static CustomModelDataComponent ChangeProjection(CustomModelDataComponent received, float value)
    {
        // Public models expose IReadOnlyList: edit a received mutable array when available, otherwise replace the immutable projection.
        if (received.Floats is float[] array)
        {
            array[0] = value;
            return received;
        }
        return received with { Floats = [value] };
    }

    private static byte[] Payload(int protocol, string scenario)
        => Convert.FromBase64String((string)NativeCases().Single(row => (int)row[0] == protocol && (string)row[1] == scenario)[2]);

    private static PacketCodecContext Context(int protocol) => new(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);

    private static ClientboundContainerSetSlotPacket Decode(int protocol, byte[] bytes)
        => Assert.IsType<ClientboundContainerSetSlotPacket>(BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot").Decode(bytes, Context(protocol)));

    private static byte[] Encode(int protocol, ClientboundContainerSetSlotPacket packet)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot").Encode(ref writer, packet, Context(protocol));
        return output.WrittenSpan.ToArray();
    }

    private const string ModernPositiveZeroPayload = "AAcAJAHnBgEADgEAAAAAAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm";
}
