using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// Send-path coverage for the client-information announce.
/// <para>The action must build the play-phase record. Encoding resolves the packet's own <see cref="PacketType"/> against the registry for the connection's current phase, so a configuration-phase type never matched the Play/Serverbound registry and the send threw <see cref="System.Net.ProtocolViolationException"/>.</para>
/// </summary>
public sealed class ClientSettingsSendTests
{
    private static SessionActions Actions(RecordingSink recorder, ClientOptions? options = null)
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        var services = new ClientSessionServices
        {
            Version = JavaVersions.V1_21_5,
            Options = options ?? new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(JavaVersions.V1_21_5),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        return new SessionActions(recorder, services);
    }

    /// <summary>The announce must be the PLAY record, so the play registry can encode it.</summary>
    [Fact]
    public async Task SetClientSettings_Sends_ThePlayPhaseRecord()
    {
        var recorder = new RecordingSink();

        await Actions(recorder).SetClientSettingsAsync();

        var sent = Assert.IsType<ServerboundPlayClientInformationPacket>(Assert.Single(recorder.Packets));
        Assert.Equal(ProtocolPhase.Play, sent.Type.Phase);
        Assert.Equal(PacketFlow.Serverbound, sent.Type.Flow);
        Assert.Equal(Identifier.Minecraft("client_information"), sent.Type.Id);
    }

    /// <summary>Every option a caller sets must reach the packet, not just locale and view distance.</summary>
    [Fact]
    public async Task SetClientSettings_Honors_EveryConfiguredField()
    {
        var recorder = new RecordingSink();
        var options = new ClientOptions
        {
            ClientInformation = new ClientInformationOptions
            {
                Locale = "es_es",
                ViewDistance = 6,
                ChatVisibility = ChatVisibility.System,
                ChatColors = false,
                DisplayedSkinParts = SkinParts.Jacket,
                MainHand = MainHand.Left,
                TextFilteringEnabled = true,
                AllowsListing = false,
                ParticleStatus = ParticleStatus.Minimal,
            },
        };

        await Actions(recorder, options).SetClientSettingsAsync();

        var sent = Assert.IsType<ServerboundPlayClientInformationPacket>(Assert.Single(recorder.Packets));
        Assert.Equal("es_es", sent.Language);
        Assert.Equal(6, sent.ViewDistance);
        Assert.Equal(1, sent.ChatVisibility);
        Assert.False(sent.ChatColors);
        Assert.Equal(0x02, sent.ModelCustomisation);
        Assert.Equal(0, sent.MainHand);
        Assert.True(sent.TextFilteringEnabled);
        Assert.False(sent.AllowsListing);
        Assert.Equal(2, sent.ParticleStatus);
    }

    /// <summary>An explicit argument overrides the session options for a one-off re-announce.</summary>
    [Fact]
    public async Task SetClientSettings_ExplicitOptions_Override_TheSessionDefaults()
    {
        var recorder = new RecordingSink();

        await Actions(recorder).SetClientSettingsAsync(
            new ClientInformationOptions { Locale = "ja_jp", ViewDistance = 32 });

        var sent = Assert.IsType<ServerboundPlayClientInformationPacket>(Assert.Single(recorder.Packets));
        Assert.Equal("ja_jp", sent.Language);
        Assert.Equal(32, sent.ViewDistance);
    }
}
