using System.Security.Cryptography;
using Xunit;

namespace Umpk.Client.Bedrock.Authentication;

public sealed class BedrockHandshakeTests
{
    [Fact]
    public void ValidHandshakeProvesKeyBindingAndDerivesSecret()
    {
        using BedrockConnectionKey client = BedrockConnectionKey.Create();
        using BedrockConnectionKey server = BedrockConnectionKey.Create();
        byte[] salt = [1, 2, 3, 4, 5, 6, 7, 8];
        string token = BedrockHandshakeTestFactory.CreateServerToken(server, salt);

        BedrockHandshakeResult result = BedrockHandshakeProcessor.Process(token, client);

        Assert.Equal(BedrockServerAuthenticationLevel.KeyPossession, result.AuthenticationLevel);
        Assert.Equal(32, result.EncryptionKey.Length);
        Assert.True(CryptographicOperations.FixedTimeEquals(
            result.EncryptionKey.Span,
            server.DeriveEncryptionKey(client.PublicKeySubjectPublicKeyInfo.Span, salt)));
        result.Dispose();
    }

    [Fact]
    public void TamperedWrongCurveAndOversizedSaltFailClosed()
    {
        using BedrockConnectionKey client = BedrockConnectionKey.Create();
        using BedrockConnectionKey server = BedrockConnectionKey.Create();
        string valid = BedrockHandshakeTestFactory.CreateServerToken(server, [1, 2, 3]);
        string tampered = valid[..^1] + (valid[^1] == 'A' ? 'B' : 'A');

        Assert.Throws<BedrockAuthenticationException>(() => BedrockHandshakeProcessor.Process(tampered, client));
        Assert.Throws<BedrockAuthenticationException>(() => BedrockHandshakeProcessor.Process(
            BedrockHandshakeTestFactory.CreateWrongCurveServerToken([1, 2, 3]), client));
        Assert.Throws<BedrockAuthenticationException>(() => BedrockHandshakeProcessor.Process(
            BedrockHandshakeTestFactory.CreateServerToken(server, new byte[65]), client));
    }

    [Fact]
    public void SignedTokenClaimIsOptional()
    {
        using BedrockConnectionKey client = BedrockConnectionKey.Create();
        using BedrockConnectionKey server = BedrockConnectionKey.Create();

        using BedrockHandshakeResult result = BedrockHandshakeProcessor.Process(
            BedrockHandshakeTestFactory.CreateServerTokenWithoutSignedToken(server, [1, 2, 3]),
            client);

        Assert.Equal(32, result.EncryptionKey.Length);
    }
}
