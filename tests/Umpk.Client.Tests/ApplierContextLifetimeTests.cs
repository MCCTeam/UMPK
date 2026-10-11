using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ApplierContextLifetimeTests
{
    [Fact]
    public async Task ConnectionAndVerificationBindingsRefreshWithoutMutatingEarlierSnapshots()
    {
        await using UmpkClient client = CreateClient();
        await using FakeJavaServer first = FakeJavaServer.Create();
        await using FakeJavaServer second = FakeJavaServer.Create();
        var capture = new CapturingApplier();
        Func<object, ProtocolPhase, int, int, CancellationToken, bool, ValueTask> apply = InstallCapture(client, capture);
        var firstSink = new PacketSink(first.ServerConnection);
        var secondSink = new PacketSink(second.ServerConnection);
        SetField(client, "_sink", firstSink);
        await ApplyAsync(apply);
        await ApplyAsync(apply);
        Assert.Same(capture.Contexts[0], capture.Contexts[1]);
        var original = (ApplierContext)capture.Contexts[0];

        SetField(client, "_sink", secondSink);
        await ApplyAsync(apply);
        var connection = (ApplierContext)capture.Contexts[2];
        Assert.NotSame(capture.Contexts[0], capture.Contexts[2]);
        Assert.Same(firstSink, original.Sink);
        Assert.Same(secondSink, connection.Sink);

        Func<Guid, SignedChatVerifier?> resolver = static _ => null;
        SetField(client, "_chatVerifiers", resolver);
        await ApplyAsync(apply);
        var verification = (ApplierContext)capture.Contexts[3];
        Assert.NotSame(capture.Contexts[2], capture.Contexts[3]);
        Assert.Null(connection.ChatVerifierResolver);
        Assert.Same(resolver, verification.ChatVerifierResolver);

        var signatures = new MessageSignatureCache();
        SetField(client, "_chatSignatureCache", signatures);
        await ApplyAsync(apply);
        await ApplyAsync(apply);
        var signature = (ApplierContext)capture.Contexts[4];
        Assert.NotSame(capture.Contexts[3], capture.Contexts[4]);
        Assert.Same(capture.Contexts[4], capture.Contexts[5]);
        Assert.Null(verification.SignatureCache);
        Assert.Same(signatures, signature.SignatureCache);
    }

    [Theory]
    [InlineData(ChatSignatureEra.V1_19)]
    [InlineData(ChatSignatureEra.V1_19_1)]
    [InlineData(ChatSignatureEra.V1_19_3)]
    public async Task SigningBindingsFollowTheCurrentCoordinator(ChatSignatureEra era)
    {
        await using UmpkClient client = CreateClient();
        var capture = new CapturingApplier();
        var apply = InstallCapture(client, capture);
        await ApplyAsync(apply);
        var unsigned = (ApplierContext)capture.Contexts[0];
        var first = Coordinator(era);
        SetField(client, "_chatSigning", first);
        await ApplyAsync(apply);
        var signing = (ApplierContext)capture.Contexts[1];
        Assert.NotSame(capture.Contexts[0], capture.Contexts[1]);
        Assert.Null(unsigned.AnnounceChatSession);
        Assert.NotNull(signing.AnnounceChatSession);
        Assert.Same(first.Tracker, signing.LastSeenTracker);
        Assert.Same(first.LegacyCollector, signing.LegacyLastSeenCollector);

        var second = Coordinator(era);
        SetField(client, "_chatSigning", second);
        await ApplyAsync(apply);
        var replacement = (ApplierContext)capture.Contexts[2];
        Assert.Same(second.Tracker, replacement.LastSeenTracker);
        Assert.Same(second.LegacyCollector, replacement.LegacyLastSeenCollector);
        Assert.Same(first.Tracker, signing.LastSeenTracker);
        Assert.Same(first.LegacyCollector, signing.LegacyLastSeenCollector);

        SetField(client, "_chatSigning", null);
        await ApplyAsync(apply);
        var ended = (ApplierContext)capture.Contexts[3];
        Assert.Null(ended.AnnounceChatSession);
        Assert.Null(ended.LastSeenTracker);
        Assert.Null(ended.LegacyLastSeenCollector);
    }

    [Fact]
    public async Task TeardownReleasesTheCachedSessionCapabilities()
    {
        await using UmpkClient client = CreateClient();
        var capture = new CapturingApplier();
        var apply = InstallCapture(client, capture);
        await ApplyAsync(apply);
        var teardown = typeof(UmpkClient).GetMethod("TeardownSessionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Task>>(client);
        await teardown();
        FieldInfo? cached = typeof(UmpkClient).GetField("_applierContext", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cached);
        Assert.Null(cached.GetValue(client));
    }

    private static UmpkClient CreateClient()
    {
        Assert.True(JavaVersions.TryGetByProtocol(777, out JavaVersion version));
        return new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.Empty, "ContextTest"))
            .UseStaticRegistries(JavaGameData.Registries(777)).Build();
    }

    private static Func<object, ProtocolPhase, int, int, CancellationToken, bool, ValueTask> InstallCapture(UmpkClient client, CapturingApplier capture)
    {
        SetField(client, "_dispatcher", new Dispatcher(new List<IApplier> { capture }));
        return typeof(UmpkClient).GetMethod("ApplyPacketAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<object, ProtocolPhase, int, int, CancellationToken, bool, ValueTask>>(client);
    }

    private static ValueTask ApplyAsync(Func<object, ProtocolPhase, int, int, CancellationToken, bool, ValueTask> apply)
        => apply(new ClientboundRotateHeadPacket(123, 0), ProtocolPhase.Play, 0, 0, CancellationToken.None, false);

    private static ChatSigningCoordinator Coordinator(ChatSignatureEra era)
        => new(Guid.Empty, Guid.Empty, era, new NoCertificates(), TimeProvider.System, NullLogger.Instance);

    private static void SetField(UmpkClient client, string name, object? value)
        => typeof(UmpkClient).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, value);

    private sealed class CapturingApplier : IApplier
    {
        public List<object> Contexts { get; } = [];

        public ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
        {
            Contexts.Add(context);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class NoCertificates : IChatSigningProvider
    {
        public ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<PlayerCertificates?>(null);
    }
}
