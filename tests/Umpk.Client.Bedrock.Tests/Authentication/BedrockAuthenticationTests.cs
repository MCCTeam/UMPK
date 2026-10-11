using System.Security.Cryptography;
using System.Text.Json;
using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.Authentication;

public sealed class BedrockAuthenticationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    private const string FixedPkcs8 = "MIG2AgEAMBAGByqGSM49AgEGBSuBBAAiBIGeMIGbAgEBBDCP+wQOoT9ORE0W/rDfCp5Wy5BRd7v9R88RB0UIb2+dFb+TU+UA0c3OHgugw6ZjrwShZANiAASBBV8ZHmOR0Y+H3PBFIjj3+PVsjRcydYWEwnlsio1w6iLOWm0fJlojAgQJsuHekCZ9s55WUiA7k0v4OwiLw3sFmjLAgA8Aw2p7ZmGCn0neETYErZeQ37Ha8jIiWju7Lio=";

    [Fact]
    public async Task OfflineArtifactsAreBoundToTheConnectionKey()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        var provider = new OfflineBedrockCredentialProvider("UMPK", Guid.Parse("40cf0c97-b20c-4b58-8e3d-d1e445f055ed"));
        var factory = new BedrockLoginArtifactFactory(new FixedTimeProvider(Now));

        BedrockLoginArtifactResult result = await factory.CreateAsync(
            Request(BedrockAuthenticationMode.Offline),
            provider,
            key,
            Skin(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Artifacts);
        Assert.DoesNotContain(FixedPkcs8, result.Artifacts.ToString(), StringComparison.Ordinal);
        BedrockOfflineMultiplayerClaims claims = BedrockLoginArtifactValidator.ValidateOffline(
            result.Artifacts,
            key.PublicKeySubjectPublicKeyInfo,
            Now);
        Assert.Equal(key.PublicKeyBase64, claims.ConnectionPublicKey);
        Assert.Equal("UMPK", claims.DisplayName);
        Assert.Equal("api://auth-minecraft-services/multiplayer", claims.Audience);
    }

    [Fact]
    public async Task OnlineUnavailableNeverCallsOfflineProvider()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        var online = new UnavailableOnlineBedrockCredentialProvider("Service trust is not configured.");
        var factory = new BedrockLoginArtifactFactory(new FixedTimeProvider(Now));

        BedrockLoginArtifactResult result = await factory.CreateAsync(
            Request(BedrockAuthenticationMode.Online),
            online,
            key,
            Skin(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BedrockAuthenticationFailureCode.OnlineAuthenticationUnavailable, result.Failure?.Code);
    }

    [Fact]
    public async Task ProviderModeMismatchFailsClosed()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        var provider = new OfflineBedrockCredentialProvider("UMPK", Guid.NewGuid());
        var factory = new BedrockLoginArtifactFactory(new FixedTimeProvider(Now));

        BedrockLoginArtifactResult result = await factory.CreateAsync(
            Request(BedrockAuthenticationMode.Online),
            provider,
            key,
            Skin(),
            CancellationToken.None);

        Assert.Equal(BedrockAuthenticationFailureCode.AuthenticationModeMismatch, result.Failure?.Code);
    }

    [Fact]
    public void ValidatorRejectsExpiredAndWrongAudienceTokens()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        BedrockLoginArtifacts expired = BedrockLoginArtifactTestFactory.CreateOffline(
            key,
            "UMPK",
            Guid.NewGuid(),
            Now.AddHours(-2),
            Now.AddHours(-1),
            "api://auth-minecraft-services/multiplayer");
        BedrockLoginArtifacts wrongAudience = BedrockLoginArtifactTestFactory.CreateOffline(
            key,
            "UMPK",
            Guid.NewGuid(),
            Now,
            Now.AddHours(1),
            "wrong-audience");

        Assert.Equal(BedrockAuthenticationFailureCode.ExpiredArtifact, Assert.Throws<BedrockAuthenticationException>(() =>
            BedrockLoginArtifactValidator.ValidateOffline(expired, key.PublicKeySubjectPublicKeyInfo, Now)).Code);
        Assert.Equal(BedrockAuthenticationFailureCode.InvalidAudience, Assert.Throws<BedrockAuthenticationException>(() =>
            BedrockLoginArtifactValidator.ValidateOffline(wrongAudience, key.PublicKeySubjectPublicKeyInfo, Now)).Code);
    }

    [Fact]
    public void CompactJwtRejectsWrongKeyAlgorithmAndDuplicateClaims()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        using BedrockConnectionKey other = BedrockConnectionKey.Create();
        string valid = BedrockLoginArtifactTestFactory.CreateOfflineToken(
            key,
            "UMPK",
            Guid.NewGuid(),
            Now,
            Now.AddHours(1),
            "api://auth-minecraft-services/multiplayer");

        Assert.Throws<BedrockAuthenticationException>(() => BedrockCompactJwt.ValidateOfflineToken(valid, other.PublicKeySubjectPublicKeyInfo, Now));
        string none = valid.Replace("eyJhbGciOiJFUzM4NCIs", "eyJhbGciOiJub25lIiw", StringComparison.Ordinal);
        Assert.Throws<BedrockAuthenticationException>(() => BedrockCompactJwt.ValidateOfflineToken(none, key.PublicKeySubjectPublicKeyInfo, Now));
        string duplicatePayload = BedrockLoginArtifactTestFactory.SignRawPayload(key, "{\"iss\":\"self\",\"iss\":\"other\",\"aud\":\"api://auth-minecraft-services/multiplayer\",\"nbf\":1800000000,\"exp\":1800003600,\"cpk\":\"x\",\"xname\":\"UMPK\",\"identity\":\"40cf0c97-b20c-4b58-8e3d-d1e445f055ed\",\"leguuid\":\"40cf0c97-b20c-4b58-8e3d-d1e445f055ed\",\"xid\":\"0\",\"mid\":\"\"}");
        Assert.Throws<BedrockAuthenticationException>(() => BedrockCompactJwt.ValidateOfflineToken(duplicatePayload, key.PublicKeySubjectPublicKeyInfo, Now));
    }

    [Fact]
    public void OfflineTokenRequiresIssuedAtInsideValidityWindow()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        Guid identity = Guid.NewGuid();
        string missing = Payload(key, identity, "\"nbf\":1800000000,\"exp\":1800003600");
        string future = Payload(key, identity, "\"nbf\":1800000000,\"exp\":1800003600,\"iat\":1800007200");

        Assert.Equal(BedrockAuthenticationFailureCode.InvalidArtifact, Assert.Throws<BedrockAuthenticationException>(() =>
            BedrockCompactJwt.ValidateOfflineToken(BedrockLoginArtifactTestFactory.SignRawPayload(key, missing), key.PublicKeySubjectPublicKeyInfo, Now)).Code);
        Assert.Equal(BedrockAuthenticationFailureCode.InvalidArtifact, Assert.Throws<BedrockAuthenticationException>(() =>
            BedrockCompactJwt.ValidateOfflineToken(BedrockLoginArtifactTestFactory.SignRawPayload(key, future), key.PublicKeySubjectPublicKeyInfo, Now)).Code);
    }

    [Fact]
    public void JwtRejectsExcessivePropertyCount()
    {
        string payload = "{" + string.Join(",", Enumerable.Range(0, 65).Select(index => $"\"p{index}\":1")) + "}";

        Assert.Throws<BedrockAuthenticationException>(() =>
            BedrockCompactJwt.ValidateUniqueProperties(System.Text.Encoding.UTF8.GetBytes(payload)));
    }

    [Fact]
    public void ClientDataRejectsOutOfRangeViewDistanceAndTrustedSkin()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        string far = SignClientData(key, ValidClientDataClaims(viewDistance: 64));
        string trusted = SignClientData(key, ValidClientDataClaims(trustedSkin: true));
        string overridden = SignClientData(key, ValidClientDataClaims(overrideSkin: true));

        Assert.Equal(BedrockAuthenticationFailureCode.InvalidClientData, Assert.Throws<BedrockAuthenticationException>(() =>
            BedrockCompactJwt.ValidateClientDataToken(far, key.PublicKeySubjectPublicKeyInfo)).Code);
        Assert.Equal(BedrockAuthenticationFailureCode.InvalidClientData, Assert.Throws<BedrockAuthenticationException>(() =>
            BedrockCompactJwt.ValidateClientDataToken(trusted, key.PublicKeySubjectPublicKeyInfo)).Code);
        Assert.Equal(BedrockAuthenticationFailureCode.InvalidClientData, Assert.Throws<BedrockAuthenticationException>(() =>
            BedrockCompactJwt.ValidateClientDataToken(overridden, key.PublicKeySubjectPublicKeyInfo)).Code);
    }

    [Fact]
    public void ClientDataCreationRejectsOutOfRangeViewDistance()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        BedrockClientDataProfile profile = ClientData() with { MaximumViewDistance = 64 };

        Assert.Equal(BedrockAuthenticationFailureCode.InvalidClientData, Assert.Throws<BedrockAuthenticationException>(() =>
            BedrockCompactJwt.CreateClientDataToken(profile, Skin(), key)).Code);
    }

    [Fact]
    public void LoginEnvelopeRejectsDuplicateSecurityProperties()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        BedrockLoginArtifacts valid = BedrockLoginArtifactTestFactory.CreateOffline(key, "UMPK", Guid.NewGuid(), Now, Now.AddHours(1), "api://auth-minecraft-services/multiplayer");
        string duplicate = valid.IdentityEnvelope.Replace("\"AuthenticationType\":2", "\"AuthenticationType\":2,\"AuthenticationType\":2", StringComparison.Ordinal);
        var artifacts = new BedrockLoginArtifacts(BedrockAuthenticationMode.Offline, duplicate, valid.ClientDataToken, valid.ExpiresAt);

        Assert.Throws<BedrockAuthenticationException>(() => BedrockLoginArtifactValidator.ValidateOffline(artifacts, key.PublicKeySubjectPublicKeyInfo, Now));
    }

    [Fact]
    public void ClientDataUsesSourceGeneratedJsonAndRequiredSkinFields()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        string token = BedrockCompactJwt.CreateClientDataToken(ClientData(), Skin(), key);
        using JsonDocument payload = BedrockLoginArtifactTestFactory.DecodePayload(token);

        Assert.Equal("1.26.45", payload.RootElement.GetProperty("GameVersion").GetString());
        Assert.Equal("skin-id", payload.RootElement.GetProperty("SkinId").GetString());
        Assert.Equal(64, payload.RootElement.GetProperty("SkinImageWidth").GetInt32());
        Assert.Equal("127.0.0.1:19132", payload.RootElement.GetProperty("ServerAddress").GetString());
        Assert.Equal(96, BedrockLoginArtifactTestFactory.DecodeSignature(token).Length);
    }

    [Fact]
    public void ClientDataRejectsMalformedSkinBeforeSigning()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        BedrockClientSkinProfile skin = Skin() with { SkinData = "" };

        Assert.Equal(BedrockAuthenticationFailureCode.InvalidClientData, Assert.Throws<BedrockAuthenticationException>(() =>
            BedrockCompactJwt.CreateClientDataToken(ClientData(), skin, key)).Code);
    }

    [Fact]
    public void DefaultSkinClientDataTokenValidatesWithinClientDataLimits()
    {
        using BedrockConnectionKey key = BedrockConnectionKey.Create();
        string token = BedrockCompactJwt.CreateClientDataToken(ClientData(), BedrockDefaultSkin.Profile, key);

        BedrockCompactJwt.ValidateClientDataToken(token, key.PublicKeySubjectPublicKeyInfo);

        Assert.True(token.Length < 768 * 1024);
    }

    [Fact]
    public async Task CredentialAcquisitionObservesCancellation()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new OfflineBedrockCredentialProvider("UMPK", Guid.NewGuid());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.AcquireAsync(
            Request(BedrockAuthenticationMode.Offline),
            key.PublicKeySubjectPublicKeyInfo,
            cancellation.Token));
    }

    [Fact]
    public async Task CredentialCompletionFromAnOldSessionIsRejected()
    {
        using BedrockConnectionKey key = ImportFixedKey();
        var provider = new DelayedCredentialProvider();
        long generation = 7;
        ValueTask<BedrockCredentialResult> acquisition = BedrockCredentialAcquisition.AcquireAsync(
            provider,
            Request(BedrockAuthenticationMode.Offline),
            key.PublicKeySubjectPublicKeyInfo,
            generation,
            () => generation,
            CancellationToken.None);
        generation++;
        provider.Complete(new BedrockIdentityArtifacts(BedrockAuthenticationMode.Offline, Guid.NewGuid(), "UMPK", "0", [], null, null));

        BedrockCredentialResult result = await acquisition;

        Assert.Equal(BedrockAuthenticationFailureCode.StaleCredentialResult, result.Failure?.Code);
    }

    private static BedrockCredentialRequest Request(BedrockAuthenticationMode mode) => new(
        mode,
        new BedrockProtocolProfile(
            new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169")),
        "127.0.0.1:19132",
        null);

    private static BedrockConnectionKey ImportFixedKey() => BedrockConnectionKey.ImportPkcs8(Convert.FromBase64String(FixedPkcs8));

    private static BedrockClientSkinProfile Skin() => new(
        "skin-id", Convert.ToBase64String(new byte[64 * 64 * 4]), 64, 64, "e30=", "", "MS4xNC4w", "", 0, 0, false, false, false, [], "wide", "", [], [], "", "");

    private static BedrockClientDataProfile ClientData() => new(
        42, 1, 1, Guid.Parse("4afdd177-62c4-4781-b302-beb9402b65b9"), "UMPK", 7, "1.26.45", "en_GB", "127.0.0.1:19132", "UMPK", 4);

    private static string Payload(BedrockConnectionKey key, Guid identity, string lifetime)
    {
        string id = identity.ToString("D");
        return "{\"iss\":\"self\",\"aud\":\"api://auth-minecraft-services/multiplayer\"," + lifetime + ",\"cpk\":\"" + key.PublicKeyBase64 + "\",\"xname\":\"UMPK\",\"identity\":\"" + id + "\",\"leguuid\":\"" + id + "\",\"xid\":\"0\",\"mid\":\"\"}";
    }

    private static BedrockClientDataClaims ValidClientDataClaims(int viewDistance = 4, bool trustedSkin = false, bool overrideSkin = false) => new()
    {
        ClientRandomId = 42,
        CurrentInputMode = 1,
        DefaultInputMode = 1,
        DeviceId = "4afdd177-62c4-4781-b302-beb9402b65b9",
        DeviceModel = "UMPK",
        DeviceOperatingSystem = 7,
        GameVersion = "1.26.45",
        LanguageCode = "en_GB",
        PlayFabId = "4afdd17762c44781",
        SelfSignedId = "4afdd177-62c4-4781-b302-beb9402b65b9",
        ServerAddress = "127.0.0.1:19132",
        ThirdPartyName = "UMPK",
        MaximumViewDistance = viewDistance,
        TrustedSkin = trustedSkin,
        OverrideSkin = overrideSkin,
        SkinId = "skin-id",
        SkinData = Convert.ToBase64String(new byte[64 * 64 * 4]),
        SkinImageWidth = 64,
        SkinImageHeight = 64,
        SkinResourcePatch = "e30=",
        SkinGeometryData = string.Empty,
        SkinGeometryDataEngineVersion = "MS4xNC4w",
        CapeData = string.Empty,
        AnimatedImageData = [],
        ArmSize = "wide",
        CapeId = string.Empty,
        PersonaPieces = [],
        PieceTintColors = [],
        SkinAnimationData = string.Empty,
        SkinColor = string.Empty,
    };

    private static string SignClientData(BedrockConnectionKey key, BedrockClientDataClaims claims) =>
        BedrockLoginArtifactTestFactory.SignRawPayload(key, System.Text.Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(claims, BedrockAuthJsonContext.Default.BedrockClientDataClaims)));

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class DelayedCredentialProvider : IBedrockCredentialProvider
    {
        private readonly TaskCompletionSource<BedrockCredentialResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BedrockAuthenticationMode Mode => BedrockAuthenticationMode.Offline;

        public async ValueTask<BedrockCredentialResult> AcquireAsync(
            BedrockCredentialRequest request,
            ReadOnlyMemory<byte> connectionPublicKeySubjectPublicKeyInfo,
            CancellationToken cancellationToken) => await _completion.Task.WaitAsync(cancellationToken);

        internal void Complete(BedrockIdentityArtifacts artifacts) => _completion.SetResult(BedrockCredentialResult.Success(artifacts));
    }
}
