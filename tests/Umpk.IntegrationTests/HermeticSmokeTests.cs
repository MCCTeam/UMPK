using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>The always-run hermetic subset of the integration project: no server, no network. These prove the project and its data references are wired and give the default <c>dotnet test</c> run something green here without booting a JVM. The live legs are <see cref="NightlyFactAttribute"/>.</summary>
public sealed class HermeticSmokeTests
{
    [Theory]
    [InlineData(47)]
    [InlineData(770)]
    [InlineData(776)]
    public void ExemplarDescriptors_ResolvePlayPhaseTables(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        ProtocolDescriptor descriptor = version!.Protocol;

        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play));
        Assert.NotEmpty(play.Packets);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NightlyGate_ParsesFlagValues(string? value, bool expected)
    {
        // Asserts the actual gate behavior against concrete inputs (not a mirror of the implementation re-evaluated against the current environment). This is the exact rule NightlyEnabled applies to UMPK_NIGHTLY, so a change to what counts as "enabled" fails here.
        Assert.Equal(expected, IntegrationConfig.IsTruthyFlag(value));
    }
}
