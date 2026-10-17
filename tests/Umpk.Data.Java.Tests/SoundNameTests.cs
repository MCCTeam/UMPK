using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class SoundNameTests
{
    [Theory]
    [InlineData(774, 1245)]
    [InlineData(775, 1302)]
    [InlineData(776, 1306)]
    [InlineData(777, 1310)]
    public void ComponentContextsResolveTheNativeSoundHolderName(int protocol, int networkId)
    {
        RegistryAccess registries = JavaGameData.Registries(protocol);
        Assert.True(registries.TryGetRegistry(Identifier.Minecraft("sound_event"), out IRegistry sounds));
        Assert.True(sounds.TryGetKey(networkId, out Identifier name));
        Assert.Equal(Identifier.Minecraft("entity.player.attack.sweep"), name);
    }

    [Theory]
    [InlineData(477, 120, "minecraft:entity.cow.ambient")]
    [InlineData(477, 794, "minecraft:entity.zombie_villager.step")]
    [InlineData(776, 450, "minecraft:entity.cow.ambient")]
    [InlineData(776, 1967, "minecraft:entity.small_sulfur_cube.eat")]
    [InlineData(777, 0, "minecraft:entity.allay.ambient_with_item")]
    [InlineData(777, 1990, "minecraft:block.red_shrub.place")]
    public void ResolvesTheDatasetNameAtTheNetworkId(int protocol, int id, string expected)
        => Assert.Equal(expected, JavaGameData.SoundName(protocol, id));

    [Fact]
    public void MissingNamesPreserveTheExistingRefusalContract()
    {
        Assert.Null(JavaGameData.SoundName(777, -1));
        Assert.Null(JavaGameData.SoundName(777, 1991));
        Assert.Null(JavaGameData.SoundName(404, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => JavaGameData.SoundName(-1, 0));
    }

    [Fact]
    public void LookupAllocatesOnlyTheRequestedName()
    {
        const int iterations = 100;
        for (int i = 0; i < iterations; i++)
            JavaGameData.SoundName(777, 1990);

        long before = GC.GetAllocatedBytesForCurrentThread();
        string? name = null;
        for (int i = 0; i < iterations; i++)
            name = JavaGameData.SoundName(777, 1990);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal("minecraft:block.red_shrub.place", name);
        Assert.True(allocated <= iterations * 128, $"Sound lookup allocated {allocated / iterations} bytes per call; budget is 128 bytes.");
    }
}
