using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class BedInteractionEmissionTests
{
    [Fact]
    public void BedOnlyEntryPreservesEveryMeasuredFactWithoutOtherInteractionContracts()
    {
        string root = Path.Combine(LayoutOracle.RepoRoot(AppContext.BaseDirectory), "data", "java");
        Dataset dataset = DatasetLoader.Load(root);
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = new Emitter(dataset).Emit();
        Assert.Contains("BedInteractions(int protocol)", files["JavaGameData.Dispatch.g.cs"].Text, StringComparison.Ordinal);
        int measured = 0;
        foreach (VersionData version in dataset.ByProtocol.Values)
        {
            string descriptor = files[$"V{version.Protocol}/Descriptor.g.cs"].Text;
            BedFacingTable? beds = BedFacingTable.Load(version);
            BedExitCellTable? cells = BedExitCellTable.Load(version);
            if (beds is null && cells is null)
            {
                Assert.DoesNotContain(" BedInteractions =>", descriptor, StringComparison.Ordinal);
                continue;
            }
            Assert.NotNull(beds);
            Assert.NotNull(cells);
            Match member = Regex.Match(descriptor, @"BedInteractions => SharedTables\.(\w+);");
            Assert.True(member.Success);
            Match blob = Regex.Match(files["SharedTables.g.cs"].Text, @"ReadOnlySpan<byte> " + member.Groups[1].Value + @" => new byte\[\] \{([^}]*)\}");
            Assert.True(blob.Success);
            byte[] bytes = Regex.Matches(blob.Groups[1].Value, @"0x([0-9A-F]{2})").Select(value => byte.Parse(value.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            using var reader = new BinaryReader(new MemoryStream(bytes));
            Assert.Equal(7, reader.Read7BitEncodedInt());
            // Reuse schema 7. The six unrelated block/item/entity/replacement sections remain empty.
            for (int section = 0; section < 6; section++)
                Assert.Equal(0, reader.Read7BitEncodedInt());
            Assert.Equal(beds.States.Count, reader.Read7BitEncodedInt());
            foreach (BedFacingEntry bed in beds.States)
            {
                Assert.Equal(bed.State, reader.Read7BitEncodedInt());
                Assert.Equal(bed.Facing, reader.ReadByte());
            }
            Assert.Equal(cells.Ranges.Length, reader.Read7BitEncodedInt());
            foreach (ReplacementRange cell in cells.Ranges)
            {
                Assert.Equal(cell.First, reader.Read7BitEncodedInt());
                Assert.Equal(cell.Last, reader.Read7BitEncodedInt());
                Assert.Equal(cell.Flags, reader.ReadByte());
            }
            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
            measured++;
        }
        Assert.Equal(14, measured);
    }
}
