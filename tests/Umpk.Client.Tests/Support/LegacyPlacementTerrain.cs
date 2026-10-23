using System.Text.Json;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.Support;

internal static class LegacyPlacementTerrain
{
    internal static async Task Receive(ApplierHarness harness, int protocol)
    {
        using JsonDocument document = LegacyPlayerMembershipRuntimeTests.ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Last(row => row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement native = document.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
        JsonElement frame = native.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("position").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == 1).GetProperty("packets")[0];
        var chunk = Assert.IsType<ClientboundLevelChunkPacket>(LegacyPlayerMembershipRuntimeTests.DecodeNative(harness, protocol, native, frame));
        Assert.True(chunk.FullChunk);
        Assert.Equal(new ChunkPos(0, 0), chunk.Column.Position);
        await harness.ApplyAsync(chunk);
        Assert.True(harness.State.EntityMembership.HasColumn(new BlockPos(1, 64, 1)));
    }
}
