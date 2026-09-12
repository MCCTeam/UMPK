using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Commands;

/// <summary>Tests for the per-version argument-type tables: the id-to-name bijection, and the 770-vs-776 id shift (26.2 inserts <c>team_color</c>/<c>hex_color</c> and <c>dialog</c>, shifting later ids).</summary>
public class ArgumentTypeRegistryTests
{
    [Fact]
    public void V1_21_5_Table_HasExpectedShape()
    {
        ArgumentTypeRegistry r = ArgumentTypeRegistry.V1_21_5;
        Assert.Equal(55, r.Count);
        Assert.Equal("brigadier:bool", r.NameFromId(0));
        Assert.Equal("minecraft:entity", r.NameFromId(6));
        Assert.Equal("minecraft:color", r.NameFromId(16));
        Assert.Equal("minecraft:uuid", r.NameFromId(54));
        Assert.Equal(6, r.IdFromName("minecraft:entity"));
        Assert.Null(r.NameFromId(55));
        Assert.Equal(-1, r.IdFromName("minecraft:nonexistent"));
    }

    [Fact]
    public void V26_2_Table_HasExpectedShape()
    {
        ArgumentTypeRegistry r = ArgumentTypeRegistry.V26_2;
        Assert.Equal(57, r.Count);
        Assert.Equal("minecraft:team_color", r.NameFromId(16));
        Assert.Equal("minecraft:hex_color", r.NameFromId(17));
        Assert.Equal("minecraft:dialog", r.NameFromId(55));
        Assert.Equal("minecraft:uuid", r.NameFromId(56));
    }

    [Fact]
    public void SharedParser_ShiftsIdBetweenVersions()
    {
        // minecraft:message is id 19 on 770 but id 20 on 776 (the two color entries pushed it down one).
        Assert.Equal(19, ArgumentTypeRegistry.V1_21_5.IdFromName("minecraft:message"));
        Assert.Equal(20, ArgumentTypeRegistry.V26_2.IdFromName("minecraft:message"));

        // minecraft:uuid is the last entry on both, at different ids.
        Assert.Equal(54, ArgumentTypeRegistry.V1_21_5.IdFromName("minecraft:uuid"));
        Assert.Equal(56, ArgumentTypeRegistry.V26_2.IdFromName("minecraft:uuid"));
    }

    [Fact]
    public void DuplicateName_Throws()
    {
        Assert.Throws<ArgumentException>(() => ArgumentTypeRegistry.Build(["a:b", "a:b"]));
    }
}
