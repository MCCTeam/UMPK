using System.Reflection;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Tests.Login;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Protocol.Java.Tests.World;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Registration;

/// <summary>The suites that carry a literal row per supported protocol must carry a row for EVERY supported protocol. This checks COVERAGE, never the expected VALUES: the values stay hand-written, because a test that derives its expectation from the data it verifies is structurally blind.</summary>
/// <remarks>The yardstick is <c>JavaProtocols</c>, the one place in the package a protocol number is given a name and therefore the one edit adding a version cannot skip. The version catalog would be the obvious source, but this assembly deliberately does not reference the dataset package.</remarks>
public sealed class AllProtocolTableCoverageTests
{
    /// <summary>Each literal table, and the protocols it is allowed not to cover, with the reason.</summary>
    public static TheoryData<string, IReadOnlyList<int>, int[]> Tables =>
        new()
        {
            { nameof(AliasCodecBindingTests), AliasCodecBindingTests.Protocols(), [] },
            { nameof(ConnectionObligationBindingTests), ConnectionObligationBindingTests.Protocols(), [] },
            { nameof(LoginCodecBindingTests), LoginCodecBindingTests.Protocols(), [] },
            { nameof(MisboundCodecFramingTests), MisboundCodecFramingTests.Protocols(), [] },
            { nameof(RespawnCodecBoundaryTests), RespawnCodecBoundaryTests.Protocols(), [] },

            // Protocol 47 has no use_item identity, and the packet arrives with 1.9.
            { nameof(UseItemFramingTests), UseItemFramingTests.Protocols(), [47] },
        };

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryLiteralWireLayoutTable_CoversEverySupportedProtocol(string table, IReadOnlyList<int> covered, int[] exempt)
    {
        int[] missing = [.. SupportedProtocols.All.Except(covered).Except(exempt).Order()];
        Assert.True(missing.Length == 0, $"{table} has no row for protocol(s) {string.Join(", ", missing)}.");

        int[] unsupported = [.. covered.Except(SupportedProtocols.All).Order()];
        Assert.True(unsupported.Length == 0, $"{table} has a row for protocol(s) {string.Join(", ", unsupported)}, which JavaProtocols does not name.");
    }
}

/// <summary>Every protocol number <c>JavaProtocols</c> names. Read by reflection because the constants have no enumeration of their own, and a hand-copied second list here would be one more table to forget.</summary>
internal static class SupportedProtocols
{
    public static IReadOnlyList<int> All { get; } =
    [
        .. typeof(JavaProtocols)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(int))
            .Select(static field => (int)field.GetRawConstantValue()!)
            .Distinct()
            .Order(),
    ];
}
