using System.Buffers;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class EntityGeometryTableTests
{
    public static IEnumerable<object[]> PoseFarmDimensions()
    {
        foreach (int protocol in new[] { 477, 480, 485, 490 })
            foreach ((string name, float width, float height) in new[] { ("cow", .9F, 1.4F), ("pig", .9F, .9F), ("sheep", .9F, 1.3F), ("chicken", .4F, .7F) })
                foreach (bool baby in new[] { false, true })
                    foreach (EntityPose pose in new[] { EntityPose.Standing, EntityPose.FallFlying, EntityPose.Sleeping, EntityPose.Swimming, EntityPose.SpinAttack, EntityPose.Crouching, EntityPose.Dying })
                        yield return [protocol, name, baby, pose, width, height];
    }

    [Theory]
    [MemberData(nameof(PoseFarmDimensions))]
    public void PoseFarmAnimalsUseNativeAgeAndPoseBounds(int protocol, string name, bool baby, EntityPose pose, float width, float height)
    {
        IEntityGeometrySource source = JavaGameData.EntityGeometryData(protocol);
        var entity = Identifier.Minecraft(name);
        Assert.True(source.TryGetRule(entity, out EntityGeometryRule rule));
        Assert.Equal(14, rule.MetadataIndex);
        Assert.Equal(6, rule.PoseMetadataIndex);
        Assert.Equal(EntityGeometrySelectorKind.Boolean, rule.SelectorKind);
        Assert.Equal(EntityDimensionsRefresh.RetainMinimumOnGrowth, rule.Refresh);
        Assert.Equal(EntityGeometryRefreshTiming.SynchronizedData, rule.RefreshTiming);
        Assert.Equal(0, rule.RequiredLoadedRadius);
        Assert.Null(rule.ScaleAttribute);
        Assert.True(source.TryGetDimensions(entity, baby, pose, out EntityGeometryDimensions dimensions));
        bool sleeping = pose == EntityPose.Sleeping;
        float scale = baby ? .5F : 1F;
        Assert.Equal(new EntityGeometryDimensions(sleeping ? .2F : width * scale, sleeping ? .2F : height * scale, sleeping, true), dimensions);
    }

    public static IEnumerable<object[]> LegacyFarmDimensions()
    {
        foreach (int protocol in new[] { 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404 })
            foreach ((string name, float width, float height) in new[] { ("cow", .9F, 1.4F), ("pig", .9F, .9F), ("sheep", .9F, 1.3F), ("chicken", .4F, .7F) })
                foreach (bool baby in new[] { false, true })
                    yield return [protocol, name, baby, baby ? width * .5F : width, baby ? height * .5F : height];
    }

    [Theory]
    [MemberData(nameof(LegacyFarmDimensions))]
    public void LegacyFarmAnimalsUseNativeBooleanAgeBounds(int protocol, string name, bool baby, float width, float height)
    {
        IEntityGeometrySource source = JavaGameData.EntityGeometryData(protocol);
        var entity = Identifier.Minecraft(name);
        Assert.True(source.TryGetRule(entity, out EntityGeometryRule rule));
        Assert.Equal(protocol <= 110 ? 11 : 12, rule.MetadataIndex);
        Assert.Equal(EntityGeometrySelectorKind.Boolean, rule.SelectorKind);
        Assert.Equal(protocol <= 210 ? EntityDimensionsRefresh.RetainMinimum : EntityDimensionsRefresh.RetainMinimumOnGrowth, rule.Refresh);
        Assert.Equal(EntityGeometryRefreshTiming.SynchronizedData, rule.RefreshTiming);
        Assert.Equal(0, rule.RequiredLoadedRadius);
        Assert.True(source.TryGetDimensions(entity, baby, EntityPose.Standing, out EntityGeometryDimensions dimensions));
        Assert.Equal(new EntityGeometryDimensions(width, height, false, true), dimensions);
    }

    [Theory]
    [InlineData("cow", false, .9F, 1.3F)]
    [InlineData("cow", true, .45F, .65F)]
    [InlineData("pig", false, .9F, .9F)]
    [InlineData("pig", true, .45F, .45F)]
    [InlineData("sheep", false, .9F, 1.3F)]
    [InlineData("sheep", true, .45F, .65F)]
    [InlineData("chicken", false, .4F, .7F)]
    [InlineData("chicken", true, .2F, .35F)]
    public void ClassicFarmAnimalsUseMeasuredAgeBounds(string name, bool baby, float width, float height)
    {
        IEntityGeometrySource source = JavaGameData.EntityGeometryData(47);
        var entity = Identifier.Minecraft(name);
        Assert.True(source.TryGetRule(entity, out EntityGeometryRule rule));
        Assert.Equal(EntityGeometrySelectorKind.NegativeByte, rule.SelectorKind);
        Assert.Equal(EntityGeometryRefreshTiming.LivingTick, rule.RefreshTiming);
        Assert.Equal(32, rule.RequiredLoadedRadius);
        Assert.True(source.TryGetDimensions(entity, baby, EntityPose.Standing, out EntityGeometryDimensions dimensions));
        Assert.Equal(new EntityGeometryDimensions(width, height, false, true), dimensions);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("negative-metadata")]
    [InlineData("pose-only-true-selector")]
    [InlineData("pose-index")]
    [InlineData("refresh")]
    [InlineData("scale")]
    [InlineData("selector")]
    [InlineData("pose")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("flags")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("trailing")]
    public void InvalidPackedGeometryIsRejected(string defect)
    {
        Assert.Throws<ProtocolViolationException>(() => JavaBlockInteractions.Create(Pack(defect)));
        byte[] standalone = [1, .. Pack(defect).AsSpan(7)];
        Assert.Throws<ProtocolViolationException>(() => JavaEntityGeometry.Create(standalone));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(210)]
    [InlineData(498)]
    [InlineData(754)]
    [InlineData(755)]
    [InlineData(777)]
    public void IndependentGeometryIsCachedAndMatchesTheInteractionAdapter(int protocol)
    {
        IEntityGeometrySource source = JavaGameData.EntityGeometryData(protocol);
        Assert.Same(source, JavaGameData.EntityGeometryData(protocol));
        IEntityGeometrySource adapter = Assert.IsAssignableFrom<IEntityGeometrySource>(JavaGameData.BlockInteractionData(protocol));
        var villager = Identifier.Minecraft("villager");
        Assert.Equal(adapter.TryGetRule(villager, out EntityGeometryRule expected), source.TryGetRule(villager, out EntityGeometryRule actual));
        Assert.Equal(expected, actual);
        foreach (bool baby in new[] { false, true })
            foreach (EntityPose pose in new[] { EntityPose.Standing, EntityPose.Sleeping, EntityPose.Unknown })
            {
                Assert.Equal(adapter.TryGetDimensions(villager, baby, pose, out EntityGeometryDimensions expectedSize), source.TryGetDimensions(villager, baby, pose, out EntityGeometryDimensions actualSize));
                Assert.Equal(expectedSize, actualSize);
            }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void IndependentGeometryRejectsUnsupportedSchemas(byte schema) => Assert.Throws<ProtocolViolationException>(() => JavaEntityGeometry.Create([schema, 0]));

    [Theory]
    [InlineData("valid")]
    [InlineData("selector")]
    [InlineData("none-index")]
    [InlineData("timing")]
    [InlineData("negative-radius")]
    [InlineData("excessive-radius")]
    [InlineData("unused-radius")]
    [InlineData("truncated")]
    [InlineData("trailing")]
    public void DeferredTablesValidateTheirSelectorAndTickPolicy(string defect)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(2);
        writer.WriteVarInt(1);
        writer.WriteString("minecraft:villager");
        writer.WriteVarInt(12);
        writer.WriteVarInt(-1);
        writer.WriteByte(2);
        writer.WriteByte(defect == "selector" ? (byte)3 : defect == "none-index" ? (byte)0 : (byte)2);
        writer.WriteByte(defect == "timing" ? (byte)2 : defect == "unused-radius" ? (byte)0 : (byte)1);
        writer.WriteVarInt(defect == "negative-radius" ? -1 : defect == "excessive-radius" ? 33 : 32);
        writer.WriteString("");
        writer.WriteVarInt(2);
        for (int selector = 0; selector < 2; selector++)
        {
            writer.WriteByte((byte)selector);
            writer.WriteByte(0);
            writer.WriteFloat(selector == 0 ? .6F : .3F);
            writer.WriteFloat(selector == 0 ? 1.8F : .9F);
            writer.WriteByte(2);
        }
        if (defect == "trailing")
            writer.WriteByte(0);
        byte[] bytes = buffer.WrittenSpan.ToArray();
        if (defect == "truncated")
            bytes = bytes[..^1];
        if (defect != "valid")
        {
            Assert.Throws<ProtocolViolationException>(() => JavaEntityGeometry.Create(bytes));
            return;
        }
        IEntityGeometrySource source = JavaEntityGeometry.Create(bytes);
        Assert.True(source.TryGetRule(Identifier.Minecraft("villager"), out EntityGeometryRule rule));
        Assert.Equal(EntityGeometrySelectorKind.NegativeByte, rule.SelectorKind);
        Assert.Equal(EntityGeometryRefreshTiming.LivingTick, rule.RefreshTiming);
        Assert.Equal(32, rule.RequiredLoadedRadius);
        Assert.True(source.TryGetDimensions(Identifier.Minecraft("villager"), true, EntityPose.Standing, out EntityGeometryDimensions dimensions));
        Assert.Equal(new EntityGeometryDimensions(.3F, .9F, false, true), dimensions);
    }

    [Fact]
    public void IndependentGeometryRejectsUnsupportedProtocols() => Assert.Throws<ArgumentOutOfRangeException>(() => JavaGameData.EntityGeometryData(-1));

    [Fact]
    public void GeometryOnlyMeasurementIsKnownWithoutInventingBlockContracts()
    {
        JavaBlockInteractions source = JavaBlockInteractions.Create(Pack("valid"));
        Assert.True(source.HasData);
        Assert.True(source.TryGetRule(Identifier.Minecraft("villager"), out _));
        Assert.False(source.TryGetBlock(Identifier.Minecraft("stone"), out _));
    }

    [Theory]
    [InlineData(107, 11, EntityDimensionsRefresh.RetainMinimum, .3F, .975F)]
    [InlineData(108, 11, EntityDimensionsRefresh.RetainMinimum, .3F, .975F)]
    [InlineData(109, 11, EntityDimensionsRefresh.RetainMinimum, .3F, .975F)]
    [InlineData(110, 11, EntityDimensionsRefresh.RetainMinimum, .3F, .975F)]
    [InlineData(210, 12, EntityDimensionsRefresh.RetainMinimum, .3F, .975F)]
    [InlineData(315, 12, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(316, 12, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(335, 12, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(338, 12, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(340, 12, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(393, 12, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(401, 12, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(404, 12, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(477, 14, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(480, 14, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(485, 14, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(490, 14, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(498, 14, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(573, 15, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(754, 15, EntityDimensionsRefresh.RetainMinimumOnGrowth, .3F, .975F)]
    [InlineData(755, 16, EntityDimensionsRefresh.Recenter, .3F, .975F)]
    [InlineData(769, 16, EntityDimensionsRefresh.Recenter, .3F, .975F)]
    [InlineData(774, 16, EntityDimensionsRefresh.Recenter, .3F, .975F)]
    [InlineData(775, 16, EntityDimensionsRefresh.Recenter, .49F, .99F)]
    [InlineData(776, 16, EntityDimensionsRefresh.Recenter, .49F, .98F)]
    [InlineData(777, 16, EntityDimensionsRefresh.Recenter, .49F, .98F)]
    public void NativeVillagerGeometryPreservesEraBoundaries(int protocol, int metadata, EntityDimensionsRefresh refresh, float babyWidth, float babyHeight)
    {
        IEntityGeometrySource source = Assert.IsAssignableFrom<IEntityGeometrySource>(JavaGameData.BlockInteractionData(protocol));
        var villager = Identifier.Minecraft("villager");
        Assert.True(source.TryGetRule(villager, out EntityGeometryRule rule));
        Assert.Equal(metadata, rule.MetadataIndex);
        Assert.Equal(refresh, rule.Refresh);
        Assert.True(source.TryGetDimensions(villager, false, EntityPose.Standing, out EntityGeometryDimensions adult));
        Assert.Equal(new EntityGeometryDimensions(.6F, 1.95F, false, true), adult);
        Assert.True(source.TryGetDimensions(villager, true, EntityPose.Standing, out EntityGeometryDimensions baby));
        Assert.Equal(new EntityGeometryDimensions(babyWidth, babyHeight, false, true), baby);
        Assert.Equal(protocol >= 477, source.TryGetDimensions(villager, true, EntityPose.Sleeping, out EntityGeometryDimensions sleeping));
        if (protocol >= 477)
            Assert.Equal(new EntityGeometryDimensions(.2F, .2F, true, true), sleeping);
        Assert.False(source.TryGetDimensions(villager, false, EntityPose.Unknown, out _));
        Assert.False(source.TryGetRule(Identifier.Minecraft("missing"), out _));
    }

    [Fact]
    public void PoseOnlyGeometryHasNoMetadataSelectorAndNoTrueVariant()
    {
        byte[] packed = Pack("pose-only");
        IEntityGeometrySource[] sources = [JavaBlockInteractions.Create(packed), JavaEntityGeometry.Create([1, .. packed.AsSpan(7)])];
        foreach (IEntityGeometrySource source in sources)
        {
            var golem = Identifier.Minecraft("iron_golem");
            Assert.True(source.TryGetRule(golem, out EntityGeometryRule rule));
            Assert.Equal(-1, rule.MetadataIndex);
            Assert.Equal(6, rule.PoseMetadataIndex);
            Assert.True(source.TryGetDimensions(golem, false, EntityPose.Standing, out EntityGeometryDimensions dimensions));
            Assert.Equal(new EntityGeometryDimensions(1.4F, 2.7F, false, true), dimensions);
            Assert.False(source.TryGetDimensions(golem, true, EntityPose.Standing, out _));
        }
    }

    private static byte[] Pack(string defect)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(5);
        for (int index = 0; index < 6; index++)
            writer.WriteVarInt(0);
        writer.WriteVarInt(1);
        writer.WriteString(defect == "pose-only" ? "minecraft:iron_golem" : "minecraft:villager");
        writer.WriteVarInt(defect == "metadata" ? 255 : defect == "negative-metadata" ? -2 : defect is "pose-only" or "pose-only-true-selector" ? -1 : 16);
        writer.WriteVarInt(defect == "pose-index" ? 255 : 6);
        writer.WriteByte(defect == "refresh" ? (byte)3 : (byte)0);
        writer.WriteString(defect == "scale" ? "minecraft:missing" : "");
        int count = defect is "missing" or "pose-only" ? 1 : 2;
        writer.WriteVarInt(count);
        for (int index = 0; index < count; index++)
        {
            writer.WriteByte(defect == "selector" ? (byte)2 : defect == "duplicate" ? (byte)0 : (byte)index);
            writer.WriteByte(defect == "pose" ? (byte)18 : (byte)0);
            writer.WriteFloat(defect == "zero" ? 0 : defect == "nan" ? float.NaN : defect == "pose-only" ? 1.4F : .6F);
            writer.WriteFloat(defect == "negative" ? -1 : defect == "infinity" ? float.PositiveInfinity : defect == "pose-only" ? 2.7F : 1.95F);
            writer.WriteByte(defect == "flags" ? (byte)4 : (byte)2);
        }
        if (defect == "trailing")
            writer.WriteByte(0);
        return buffer.WrittenSpan.ToArray();
    }
}
