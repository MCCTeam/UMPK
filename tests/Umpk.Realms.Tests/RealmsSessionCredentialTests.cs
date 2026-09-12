using Umpk.Auth;
using Xunit;

namespace Umpk.Realms.Tests;

/// <summary>Pins the split in <see cref="RealmsSessionCredential.FromSession"/>: a null session is a caller bug (<see cref="ArgumentNullException"/>), while a non-Microsoft session is a fact about the world (a classified <see cref="RealmsException"/> with <see cref="RealmsErrorKind.RequiresMicrosoftAccount"/>). Realms authenticates with the Minecraft-services token from the Microsoft XBL/XSTS chain; an offline or Yggdrasil session never produces one.</summary>
public sealed class RealmsSessionCredentialTests
{
    private const string ClientVersion = "1.21.11";

    private static JavaSession NewSession(AuthKind kind) => new(
        new GameProfile(Guid.NewGuid(), "Dinnerbone"),
        "TOKEN",
        DateTimeOffset.UtcNow.AddHours(1),
        RefreshToken: null,
        kind);

    [Theory]
    [InlineData(AuthKind.Offline)]
    [InlineData(AuthKind.Yggdrasil)]
    public void NonMicrosoftSession_ThrowsRequiresMicrosoftAccount(AuthKind kind)
    {
        JavaSession session = NewSession(kind);

        RealmsException ex = Assert.Throws<RealmsException>(() => RealmsSessionCredential.FromSession(session, ClientVersion));

        Assert.Equal(RealmsErrorKind.RequiresMicrosoftAccount, ex.Kind);
    }

    [Fact]
    public void MicrosoftSession_Builds()
    {
        JavaSession session = NewSession(AuthKind.Microsoft);

        RealmsSessionCredential credential = RealmsSessionCredential.FromSession(session, ClientVersion);

        Assert.Equal(session.AccessToken, credential.AccessToken);
        Assert.Equal(session.Profile.Id, credential.ProfileId);
        Assert.Equal(session.Profile.Name, credential.Username);
        Assert.Equal(ClientVersion, credential.ClientVersion);
    }

    [Fact]
    public void NullSession_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RealmsSessionCredential.FromSession(null!, ClientVersion));
    }
}
