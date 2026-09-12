using Umpk.Auth;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class OfflineIdentityTests
{
    // Precomputed vanilla Java UUID.nameUUIDFromBytes("OfflinePlayer:<name>") values (version 3, MD5). Cross-checked against the widely published vanilla offline UUIDs (e.g. Notch).
    [Theory]
    [InlineData("Notch", "b50ad385-829d-3141-a216-7e7d7539ba7f")]
    [InlineData("Steve", "5627dd98-e6be-3c21-b8a8-e92344183641")]
    [InlineData("Player", "a01e3843-e521-3998-958a-f459800e4d11")]
    [InlineData("jeb_", "a762f560-4fce-3236-812a-b80efff0b62b")]
    [InlineData("Alex", "36532b5e-c442-3dbb-a24c-c7e55d0f979a")]
    public void ComputeUuid_MatchesVanillaJavaSemantics(string name, string expected)
    {
        Guid uuid = OfflineIdentity.ComputeUuid(name);
        Assert.Equal(expected, uuid.ToString().ToLowerInvariant());
    }

    [Fact]
    public void ComputeUuid_IsDeterministic()
    {
        Assert.Equal(OfflineIdentity.ComputeUuid("Dinnerbone"), OfflineIdentity.ComputeUuid("Dinnerbone"));
    }

    [Fact]
    public void ComputeUuid_HasVersion3AndRfcVariant()
    {
        Guid uuid = OfflineIdentity.ComputeUuid("Notch");
        byte[] bytes = uuid.ToByteArray(bigEndian: true);
        Assert.Equal(0x30, bytes[6] & 0xF0);           // version 3
        Assert.Equal(0x80, bytes[8] & 0xC0);           // RFC 4122 variant
    }

    [Fact]
    public void ComputeProfile_CarriesNameAndDeterministicUuid()
    {
        GameProfile profile = OfflineIdentity.ComputeProfile("Notch");
        Assert.Equal("Notch", profile.Name);
        Assert.Equal(new Guid("b50ad385-829d-3141-a216-7e7d7539ba7f"), profile.Id);
    }

    [Fact]
    public async Task OfflineFlow_ProducesDeterministicSessionAndDoesNotCache()
    {
        var store = new InMemoryTokenStore();
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.Offline,
            OfflineUsername = "Notch",
            TokenStore = store,
        });

        var interaction = new Fakes.FakeAuthInteraction();
        JavaSession session = await flow.LoginAsync(interaction, CancellationToken.None);

        Assert.Equal(AuthKind.Offline, session.Kind);
        Assert.Equal(new Guid("b50ad385-829d-3141-a216-7e7d7539ba7f"), session.Profile.Id);
        Assert.Equal(string.Empty, session.AccessToken);
        // Offline sessions are not persisted.
        Assert.Null(await store.GetAsync<JavaSession>("session:NOTCH", CancellationToken.None));
    }
}
