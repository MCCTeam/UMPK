using Umpk.Data.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The suites in this assembly that carry a literal row per supported protocol must carry a row for EVERY supported protocol. This checks COVERAGE, never the expected VALUES: the values stay hand-written, because a test that derives its expectation from the data it verifies is structurally blind.</summary>
/// <remarks><c>PhysicsProfileConformanceTests</c> is not listed: its two literal tables already assert their own coverage against the catalog, and against the production list rather than only the catalog, which is the stronger check.</remarks>
public sealed class AllProtocolTableCoverageTests
{
    public static TheoryData<string, IReadOnlyList<int>> Tables =>
        new()
        {
            { nameof(RespawnSendTests), RespawnSendTests.Protocols() },
            { nameof(SlabRestCompatibilityTests), SlabRestCompatibilityTests.Protocols() },
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
