using Umpk.Data.Java;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>The live matrix names one representative release per supported protocol. A version added without a representative would be silently untested here: the legs are driven off this list, so a missing row removes a leg rather than failing one.</summary>
public sealed class AllProtocolTableCoverageTests
{
    public static TheoryData<string, IReadOnlyList<int>> Tables =>
        new()
        {
            { nameof(LiveMatrix), LiveMatrix.Protocols() },
        };

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryLiteralWireLayoutTable_CoversEverySupportedProtocol(string table, IReadOnlyList<int> covered)
    {
        int[] supported = [.. JavaVersions.All.Select(static version => version.Version.Protocol).Distinct().Order()];

        int[] missing = [.. supported.Except(covered).Order()];
        Assert.True(missing.Length == 0, $"{table} has no row for protocol(s) {string.Join(", ", missing)}.");

        int[] unsupported = [.. covered.Except(supported).Order()];
        Assert.True(unsupported.Length == 0, $"{table} has a row for unsupported protocol(s) {string.Join(", ", unsupported)}.");
    }
}
