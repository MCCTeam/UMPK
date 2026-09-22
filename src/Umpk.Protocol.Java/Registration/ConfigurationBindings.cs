using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>Configuration-phase timelines (1.20.2+): registry sync, tags, features, resource packs, and the shared cookie/custom-payload/ping traffic.</summary>
internal static class ConfigurationBindings
{
    /// <summary>Adds this family's packet timelines to the binding table.</summary>
    public static void Register(PacketBindings bindings)
    {
        ConfigurationCodecs.DeclareClearDialogConfiguration(bindings);
        ConfigurationCodecs.DeclareClientInformationConfiguration(bindings);
        ConfigurationCodecs.DeclareCookieRequestConfiguration(bindings);
        ConfigurationCodecs.DeclareCookieResponseConfiguration(bindings);
        ConfigurationCodecs.DeclareCustomClickActionConfiguration(bindings);
        ConfigurationCodecs.DeclareCustomPayload(bindings);
        ConfigurationCodecs.DeclareCustomReportDetailsConfiguration(bindings);
        ConfigurationCodecs.DeclareDisconnectConfiguration(bindings);
        ConfigurationCodecs.DeclareFinishConfiguration(bindings);
        ConfigurationCodecs.DeclareKeepAliveConfiguration(bindings);
        ConfigurationCodecs.DeclarePingConfiguration(bindings);
        ConfigurationCodecs.DeclarePongConfiguration(bindings);
        ConfigurationCodecs.DeclareRegistryData(bindings);
        ConfigurationCodecs.DeclareResetChat(bindings);
        ConfigurationCodecs.DeclareResourcePackConfiguration(bindings);
        ConfigurationCodecs.DeclareResourcePackPopConfiguration(bindings);
        ConfigurationCodecs.DeclareResourcePackPushConfiguration(bindings);
        ConfigurationCodecs.DeclareSelectKnownPacks(bindings);
        ConfigurationCodecs.DeclareServerLinksConfiguration(bindings);
        ConfigurationCodecs.DeclareShowDialogConfiguration(bindings);
        ConfigurationCodecs.DeclarePostEffectsConfiguration(bindings);
        ConfigurationCodecs.DeclareStoreCookieConfiguration(bindings);
        ConfigurationCodecs.DeclareTransferConfiguration(bindings);
        ConfigurationCodecs.DeclareUpdateEnabledFeatures(bindings);
        ConfigurationCodecs.DeclareUpdateTags(bindings);
    }
}
