using Xunit;

namespace Umpk.Client.Bedrock.Bootstrap;

public sealed class BedrockBootstrapPolicyTests
{
    [Fact]
    public void OfflineNoEncryptionFlowBecomesReadyAfterAcceptedInitialization()
    {
        BedrockBootstrapState state = BedrockBootstrapState.Initial;
        state = Apply(state, BedrockBootstrapEvent.Connected(), BedrockBootstrapEffectKind.SendNetworkSettingsRequest);
        state = Apply(state, BedrockBootstrapEvent.NetworkSettingsAccepted(), BedrockBootstrapEffectKind.InstallCompression, BedrockBootstrapEffectKind.AcquireCredentials);
        state = Apply(state, BedrockBootstrapEvent.CredentialsReady(), BedrockBootstrapEffectKind.SendLogin);
        state = Apply(state, BedrockBootstrapEvent.LoginSucceeded());
        state = Apply(state, BedrockBootstrapEvent.ResourceNegotiationCompleted());
        state = Apply(state, BedrockBootstrapEvent.ItemRegistryPublished());
        state = Apply(state, BedrockBootstrapEvent.BiomeRegistryPublished());
        state = Apply(state, BedrockBootstrapEvent.EntityRegistryPublished());
        state = Apply(state, BedrockBootstrapEvent.PlayerSpawnAuthorized());
        state = Apply(state, BedrockBootstrapEvent.StartGame(123), BedrockBootstrapEffectKind.SendLocalPlayerInitialized);
        state = Apply(state, BedrockBootstrapEvent.LocalPlayerInitializationAccepted(123), BedrockBootstrapEffectKind.PublishReady);

        Assert.True(state.IsReady);
        Assert.Equal(123UL, state.LocalRuntimeEntityId);
    }

    [Fact]
    public void EncryptedFlowInstallsTransformsBeforeSendingClientHandshake()
    {
        BedrockBootstrapState state = ReachLoginSent();

        BedrockBootstrapTransition transition = BedrockBootstrapPolicy.Apply(state, BedrockBootstrapEvent.ServerHandshakeValidated());

        Assert.Equal(
            [BedrockBootstrapEffectKind.InstallEncryption, BedrockBootstrapEffectKind.SendClientHandshake],
            transition.Effects.Select(effect => effect.Kind));
        Assert.False(transition.State.IdentityAccepted);
    }

    [Fact]
    public void LoginBeforeNetworkSettingsAndDuplicateSettingsAreTerminal()
    {
        BedrockBootstrapTransition earlyLogin = BedrockBootstrapPolicy.Apply(BedrockBootstrapState.Initial, BedrockBootstrapEvent.CredentialsReady());
        BedrockBootstrapState settings = Apply(
            Apply(BedrockBootstrapState.Initial, BedrockBootstrapEvent.Connected()),
            BedrockBootstrapEvent.NetworkSettingsAccepted(),
            BedrockBootstrapEffectKind.InstallCompression,
            BedrockBootstrapEffectKind.AcquireCredentials);
        BedrockBootstrapTransition duplicate = BedrockBootstrapPolicy.Apply(settings, BedrockBootstrapEvent.NetworkSettingsAccepted());

        Assert.Equal(BedrockBootstrapFailureCode.IllegalTransition, earlyLogin.Failure?.Code);
        Assert.True(earlyLogin.State.IsTerminal);
        Assert.Equal(BedrockBootstrapFailureCode.DuplicateObligation, duplicate.Failure?.Code);
    }

    [Fact]
    public void SpawnMayPrecedeStartGameButInitializationIsEmittedOnce()
    {
        BedrockBootstrapState state = ReachStreaming();
        state = Apply(state, BedrockBootstrapEvent.ItemRegistryPublished());
        state = Apply(state, BedrockBootstrapEvent.BiomeRegistryPublished());
        state = Apply(state, BedrockBootstrapEvent.EntityRegistryPublished());
        state = Apply(state, BedrockBootstrapEvent.PlayerSpawnAuthorized());

        BedrockBootstrapTransition first = BedrockBootstrapPolicy.Apply(state, BedrockBootstrapEvent.StartGame(9));
        BedrockBootstrapTransition duplicateSpawn = BedrockBootstrapPolicy.Apply(first.State, BedrockBootstrapEvent.PlayerSpawnAuthorized());

        Assert.Equal(BedrockBootstrapEffectKind.SendLocalPlayerInitialized, Assert.Single(first.Effects).Kind);
        Assert.Empty(duplicateSpawn.Effects);
    }

    [Fact]
    public void DuplicateRegistryPublicationIsIdempotent()
    {
        BedrockBootstrapState state = ReachStreaming();
        state = Apply(state, BedrockBootstrapEvent.ItemRegistryPublished());
        state = Apply(state, BedrockBootstrapEvent.BiomeRegistryPublished());

        BedrockBootstrapTransition duplicate = BedrockBootstrapPolicy.Apply(state, BedrockBootstrapEvent.ItemRegistryPublished());

        Assert.Null(duplicate.Failure);
        Assert.True(duplicate.State.ItemRegistryAvailable);
        Assert.True(duplicate.State.BiomeRegistryAvailable);
        Assert.False(duplicate.State.IsTerminal);
        Assert.Empty(duplicate.Effects);
    }

    [Fact]
    public void DisconnectIsTerminalFromEveryBootstrapStage()
    {
        BedrockBootstrapState[] states = [BedrockBootstrapState.Initial, Apply(BedrockBootstrapState.Initial, BedrockBootstrapEvent.Connected()), ReachLoginSent(), ReachStreaming()];

        foreach (BedrockBootstrapState state in states)
        {
            BedrockBootstrapTransition transition = BedrockBootstrapPolicy.Apply(state, BedrockBootstrapEvent.Disconnected("closed"));
            Assert.True(transition.State.IsTerminal);
            Assert.Equal(BedrockBootstrapFailureCode.RemoteDisconnect, transition.Failure?.Code);
        }
    }

    private static BedrockBootstrapState ReachLoginSent()
    {
        BedrockBootstrapState state = Apply(BedrockBootstrapState.Initial, BedrockBootstrapEvent.Connected());
        state = Apply(state, BedrockBootstrapEvent.NetworkSettingsAccepted());
        return Apply(state, BedrockBootstrapEvent.CredentialsReady());
    }

    private static BedrockBootstrapState ReachStreaming()
    {
        BedrockBootstrapState state = ReachLoginSent();
        state = Apply(state, BedrockBootstrapEvent.LoginSucceeded());
        return Apply(state, BedrockBootstrapEvent.ResourceNegotiationCompleted());
    }

    private static BedrockBootstrapState Apply(BedrockBootstrapState state, BedrockBootstrapEvent @event, params BedrockBootstrapEffectKind[] expected)
    {
        BedrockBootstrapTransition transition = BedrockBootstrapPolicy.Apply(state, @event);
        Assert.Null(transition.Failure);
        if (expected.Length > 0)
            Assert.Equal(expected, transition.Effects.Select(effect => effect.Kind));
        return transition.State;
    }
}
