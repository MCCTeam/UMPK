using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    [Fact]
    public async Task CreativeMiningOwnedSetupWaitsForNativeQueriesAndTicks()
    {
        // Constructed command responses test the fixture owner; they are not native traffic or a live acceptance result.
        const string presence = "execute unless entity @e[type=!minecraft:player]";
        const string actor = "data get entity @e[type=!minecraft:player,limit=1]";
        const string witness = "Rabbit has the following entity data: {UUID: [I; 1, 2, 3, 4], Pos: [0.0d, 64.0d, 0.0d], Health: 3.0f}";
        var script = new Queue<(string Command, string Response)>([
            ("time query gametime", "The time is 100"), (presence, "Test failed, count: 2"), (actor, witness), ("kill @e[type=!minecraft:player]", "Killed 2 entities"),
            ("time query gametime", "The time is 101"), (presence, "Test passed"), ("time query gametime", "The time is 102"),
            ("time query gametime", "The time is 125"), (presence, "Test failed, count: 1"), (actor, "No entity was found"), ("kill @e[type=!minecraft:player]", "No entity was found"),
            ("time query gametime", "The time is 126"), (presence, "Test passed"), ("time query gametime", "The time is 127"),
            ("time query gametime", "The time is 146"), (presence, "Test passed"), ("time query gametime", "The time is 147"),
            ("time query gametime", "The time is 147"), (presence, "Test passed"), ("time query gametime", "The time is 148")
        ]);
        IReadOnlyList<CreativeMiningSetupPoll> evidence = [];
        Task<string> Command(string command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            (string expected, string response) = script.Dequeue();
            Assert.Equal(expected, command);
            return Task.FromResult(response);
        }
        await SettleCreativeMiningNativeEntitiesAsync(Command, rows => { evidence = rows; return Task.CompletedTask; }, CancellationToken.None, delay: CompleteCreativeMiningSetupDelayForFixtureTest);
        Assert.Empty(script);
        Assert.Equal(6, evidence.Count);
        Assert.Equal(new int?[] { 2, 0, 1, 0, 0, 0 }, evidence.Select(row => row.NativeEntityCount));
        Assert.Equal(witness, evidence[0].ActorDataResponse);
        Assert.Equal("No entity was found", evidence[2].ActorDataResponse);
        Assert.Equal("No entity was found", evidence[2].CleanupResponse);
        Assert.All(evidence.Take(5), row => Assert.False(row.Ready));
        Assert.Equal(19, evidence[4].EmptyTicks);
        Assert.Equal(20, evidence[5].EmptyTicks);
        Assert.True(evidence[5].Ready);
    }

    [Fact]
    public async Task CreativeMiningOwnedSetupNeverEmptyFailsWithinBound()
    {
        // A changing native clock cannot substitute for the global absence predicate.
        const string witness = "Falling Block has the following entity data: {UUID: [I; 4, 3, 2, 1], Pos: [0.5d, 64.0d, 0.5d], Time: 0}";
        int commands = 0, clocks = 0;
        IReadOnlyList<CreativeMiningSetupPoll> evidence = [];
        Task<string> Command(string command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            commands++;
            string response = command switch
            {
                "time query gametime" => "The time is " + (++clocks * 20).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "execute unless entity @e[type=!minecraft:player]" => "Test failed, count: 1",
                "data get entity @e[type=!minecraft:player,limit=1]" => witness,
                "kill @e[type=!minecraft:player]" => "Killed Falling Block",
                _ => throw new InvalidOperationException("Unexpected fixture command: " + command)
            };
            return Task.FromResult(response);
        }
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => SettleCreativeMiningNativeEntitiesAsync(Command, rows => { evidence = rows; return Task.CompletedTask; }, CancellationToken.None, delay: CompleteCreativeMiningSetupDelayForFixtureTest));
        Assert.Equal("Native global no-peer precondition did not settle within 24 probes.", error.Message);
        Assert.Equal(24, evidence.Count);
        Assert.Equal(96, commands);
        Assert.Equal(24, clocks);
        Assert.All(evidence, row =>
        {
            Assert.Equal(1, row.NativeEntityCount);
            Assert.Equal(witness, row.ActorDataResponse);
            Assert.Equal("Killed Falling Block", row.CleanupResponse);
            Assert.False(row.Ready);
        });
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task CreativeMiningOwnedSetupUsesReleaseClockAndWrap(int protocol)
    {
        // Constructed native command text tests exact release grammar and the existing 20-tick absence fence across wrap.
        string Clock(long tick) => protocol == 777 ? FormattableString.Invariant($"The game time is {tick} tick(s)") : FormattableString.Invariant($"The time is {tick % int.MaxValue}");
        const long modulus = int.MaxValue;
        var script = new Queue<(string Command, string Response)>([
            ("time query gametime", Clock(modulus - 2)), ("execute unless entity @e[type=!minecraft:player]", "Test passed"), ("time query gametime", Clock(modulus - 1)),
            ("time query gametime", Clock(modulus + 18)), ("execute unless entity @e[type=!minecraft:player]", "Test passed"), ("time query gametime", Clock(modulus + 19)),
            ("time query gametime", Clock(modulus + 19)), ("execute unless entity @e[type=!minecraft:player]", "Test passed"), ("time query gametime", Clock(modulus + 20))
        ]);
        IReadOnlyList<CreativeMiningSetupPoll> evidence = [];
        Task<string> Command(string command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            (string expected, string response) = script.Dequeue();
            Assert.Equal(expected, command);
            return Task.FromResult(response);
        }
        await SettleCreativeMiningNativeEntitiesAsync(Command, rows => { evidence = rows; return Task.CompletedTask; }, CancellationToken.None, protocol: protocol, delay: CompleteCreativeMiningSetupDelayForFixtureTest);
        Assert.Empty(script);
        Assert.Equal(new long?[] { 0, 19, 20 }, evidence.Select(row => row.EmptyTicks));
        Assert.Equal(new int?[] { int.MaxValue - 2, 18, 19 }, evidence.Select(row => row.NativeTickBefore));
        Assert.All(evidence.Take(2), row => Assert.False(row.Ready));
        Assert.True(evidence[2].Ready);
        Assert.Equal(0, ParseCreativeMiningNativeTick(Clock(0), protocol));
        Assert.Equal(int.MaxValue - 1, ParseCreativeMiningNativeTick(Clock(int.MaxValue - 1), protocol));
        if (protocol == 777)
            Assert.Equal((int)(long.MaxValue % int.MaxValue), ParseCreativeMiningNativeTick(Clock(long.MaxValue), protocol));
        string prefix = protocol == 777 ? "The game time is " : "The time is ";
        string suffix = protocol == 777 ? " tick(s)" : string.Empty;
        string overflow = protocol == 777 ? "9223372036854775808" : "2147483647";
        foreach (string invalid in new[] { "-1", "+1", "01", "1.0", "", overflow })
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ParseCreativeMiningNativeTick(prefix + invalid + suffix, protocol));
        string wrongEraText = protocol == 777 ? "The time is 1" : "The game time is 1 tick(s)";
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ParseCreativeMiningNativeTick(wrongEraText, protocol));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ParseCreativeMiningNativeTick(Clock(1) + " trailing", protocol));
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task CreativeMiningOwnedSetupUsesReleaseEntityCountAndRejectsWrongPrefix(int protocol)
    {
        // Constructed command text tests the exact release count grammar before actor inspection and cleanup.
        string Clock(long tick) => protocol == 777 ? FormattableString.Invariant($"The game time is {tick} tick(s)") : FormattableString.Invariant($"The time is {tick}");
        string failedPrefix = protocol == 777 ? "Test failed. Count: " : "Test failed, count: ";
        const string presence = "execute unless entity @e[type=!minecraft:player]";
        const string actor = "data get entity @e[type=!minecraft:player,limit=1]";
        const string witness = "Rabbit has the following entity data: {UUID: [I; 1, 2, 3, 4], Pos: [0.0d, 64.0d, 0.0d], Health: 3.0f}";
        var script = new Queue<(string Command, string Response)>([
            ("time query gametime", Clock(100)), (presence, failedPrefix + "2"), (actor, witness), ("kill @e[type=!minecraft:player]", "Killed 2 entities"),
            ("time query gametime", Clock(101)), (presence, "Test passed"), ("time query gametime", Clock(102)),
            ("time query gametime", Clock(121)), (presence, "Test passed"), ("time query gametime", Clock(122)),
            ("time query gametime", Clock(122)), (presence, "Test passed"), ("time query gametime", Clock(123))
        ]);
        IReadOnlyList<CreativeMiningSetupPoll> evidence = [];
        Task<string> Command(string command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            (string expected, string response) = script.Dequeue();
            Assert.Equal(expected, command);
            return Task.FromResult(response);
        }
        await SettleCreativeMiningNativeEntitiesAsync(Command, rows => { evidence = rows; return Task.CompletedTask; }, CancellationToken.None, protocol: protocol, delay: CompleteCreativeMiningSetupDelayForFixtureTest);
        Assert.Empty(script);
        Assert.Equal(new int?[] { 2, 0, 0, 0 }, evidence.Select(row => row.NativeEntityCount));
        Assert.Equal(witness, evidence[0].ActorDataResponse);
        Assert.Equal("Killed 2 entities", evidence[0].CleanupResponse);
        Assert.Equal(new long?[] { null, 0, 19, 20 }, evidence.Select(row => row.EmptyTicks));
        Assert.All(evidence.Take(3), row => Assert.False(row.Ready));
        Assert.True(evidence[3].Ready);

        string wrongEra = protocol == 777 ? "Test failed, count: 1" : "Test failed. Count: 1";
        foreach (string invalid in new[] { wrongEra, "Test failed count: 1", "Test failed, Count: 1" })
        {
            var commands = new List<string>();
            evidence = [];
            Task<string> InvalidCommand(string command, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                commands.Add(command);
                return Task.FromResult(command == "time query gametime" ? Clock(100) : invalid);
            }
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() => SettleCreativeMiningNativeEntitiesAsync(InvalidCommand, rows => { evidence = rows; return Task.CompletedTask; }, CancellationToken.None, protocol: protocol, delay: CompleteCreativeMiningSetupDelayForFixtureTest));
            Assert.Equal(new[] { "time query gametime", presence }, commands);
            CreativeMiningSetupPoll rejected = Assert.Single(evidence);
            Assert.Equal(invalid, rejected.PresenceResponse);
            Assert.Null(rejected.NativeEntityCount);
            Assert.Null(rejected.ActorDataResponse);
            Assert.Null(rejected.CleanupResponse);
            Assert.False(rejected.Ready);
        }
    }

    private static Task CompleteCreativeMiningSetupDelayForFixtureTest(TimeSpan interval, CancellationToken ct)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), interval);
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
