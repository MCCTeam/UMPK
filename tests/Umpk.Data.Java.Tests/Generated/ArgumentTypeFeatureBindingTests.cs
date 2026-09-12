using System.Text.Json;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Data.Java.Tests.Generated;

/// <summary>the command-tree argument-type table is selected off the dataset-driven ProtocolFeatures flag (<c>argumentTypeEra</c>), not a protocol-number compare in client code. Pins that the generated descriptors carry the correct era so the applier resolves the right registry.</summary>
public sealed class ArgumentTypeFeatureBindingTests
{
    [Fact]
    public void V1_21_5_Selects_V1_21_5_ArgumentTypes()
    {
        Assert.Equal("v1_21_5", JavaVersions.V1_21_5.Features.ArgumentTypeEra);
        Assert.Same(ArgumentTypeRegistry.V1_21_5, JavaVersions.V1_21_5.Features.ArgumentTypes);
    }

    [Fact]
    public void V26_2_Selects_V26_2_ArgumentTypes()
    {
        Assert.Equal("v26_2", JavaVersions.V26_2.Features.ArgumentTypeEra);
        Assert.Same(ArgumentTypeRegistry.V26_2, JavaVersions.V26_2.Features.ArgumentTypes);
    }

    /// <summary>The era flag must resolve the table the DATASET actually records for that protocol. The flag and the packet timeline are two independent selection paths for the same table, and until this test existed the flag silently fell through to the 1.21.5 table for every era it did not name: 759-763, 768-769 and 771-775 all resolved a registry whose parser ids do not match their own wire.</summary>
    [Theory]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    [InlineData(762)]
    [InlineData(763)]
    [InlineData(764)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    public void ArgumentTypeWireLayoutFlag_ResolvesTheDatasetTable(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        ArgumentTypeRegistry registry = version!.Protocol.Features.ArgumentTypes;
        IReadOnlyList<string> expected = DatasetArgumentTypes(protocol);

        Assert.Equal(expected.Count, registry.Count);
        for (int id = 0; id < expected.Count; id++)
        {
            Assert.Equal(expected[id], registry.NameFromId(id));
        }
    }

    private static IReadOnlyList<string> DatasetArgumentTypes(int protocol)
    {
        string path = Path.Combine(
            LocateDataRoot(),
            protocol.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "argument_types.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        var byId = new SortedDictionary<int, string>();
        foreach (JsonElement entry in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            byId.Add(entry.GetProperty("id").GetInt32(), entry.GetProperty("name").GetString()!);
        }

        Assert.Equal(Enumerable.Range(0, byId.Count), byId.Keys);
        return [.. byId.Values];
    }

    private static string LocateDataRoot()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null && !Directory.Exists(Path.Combine(cursor.FullName, "data", "java")))
        {
            cursor = cursor.Parent;
        }

        return cursor is null
            ? throw new DirectoryNotFoundException("could not locate data/java from the test output dir")
            : Path.Combine(cursor.FullName, "data", "java");
    }
}
