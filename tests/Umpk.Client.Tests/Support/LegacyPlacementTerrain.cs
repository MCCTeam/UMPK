using System.Buffers.Binary;
using System.Text.Json;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.Support;

internal static class LegacyPlacementTerrain
{
    internal static async Task Receive(ApplierHarness harness, int protocol, int chunkX = 0, int chunkZ = 0)
    {
        using JsonDocument document = LegacyPlayerMembershipRuntimeTests.ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Last(row => row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement native = document.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
        JsonElement frame = native.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("position").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == 1).GetProperty("packets")[0];
        var chunk = Assert.IsType<ClientboundLevelChunkPacket>(LegacyPlayerMembershipRuntimeTests.DecodeNative(harness, protocol, native, frame));
        if (chunkX != 0 || chunkZ != 0)
        {
            byte[] body = chunk.RawBody!.ToArray();
            BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(0, 4), chunkX);
            BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(4, 4), chunkZ);
            chunk = Assert.IsType<ClientboundLevelChunkPacket>(BoundDescriptorCodec.RoundTrip(protocol, "level_chunk_with_light", chunk with { RawBody = body }));
        }
        Assert.True(chunk.FullChunk);
        Assert.Equal(new ChunkPos(chunkX, chunkZ), chunk.Column.Position);
        await harness.ApplyAsync(chunk);
        Assert.True(harness.State.EntityMembership.HasColumn(new BlockPos(chunkX * 16 + 1, 64, chunkZ * 16 + 1)));
    }
}
