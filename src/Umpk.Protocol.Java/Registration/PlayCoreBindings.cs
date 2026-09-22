using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>Core play-phase timelines: join-game, keep-alive, the chat cluster (legacy, signed, player, system, disguised), and the level chunk.</summary>
internal static class PlayCoreBindings
{
    /// <summary>Adds this family's packet timelines to the binding table.</summary>
    public static void Register(PacketBindings bindings)
    {
        ChatCodecs.DeclareChat(bindings);
        ChatCodecs.DeclareChatAck(bindings);
        ChatCodecs.DeclareChatSessionUpdate(bindings);
        ChatCodecs.DeclarePlayerChat(bindings);
        ChatCommandCodecs.DeclareChatCommand(bindings);
        ChatCommandCodecs.DeclareChatCommandSigned(bindings);
        ChatDisplayCodecs.DeclareDisguisedChat(bindings);
        ChatDisplayCodecs.DeclareSystemChat(bindings);
        ChunkCodecs.DeclareLevelChunkWithLight(bindings);
        ChunkCodecs.DeclareMapChunkBulk(bindings);
        ConfigurationCodecs.DeclareConfigurationAcknowledged(bindings);
        ConfigurationCodecs.DeclareStartConfiguration(bindings);
        JoinGameCodecs.DeclareLogin(bindings);
        PlayClientInformationCodecs.DeclareClientInformationPlay(bindings);
        PlayCommonCodecs.DeclareBundleDelimiter(bindings);
        PlayCommonCodecs.DeclareClientCommand(bindings);
        PlayCommonCodecs.DeclareClientTickEnd(bindings);
        PlayCommonCodecs.DeclareCookieRequestPlay(bindings);
        PlayCommonCodecs.DeclareCookieResponsePlay(bindings);
        PlayCommonCodecs.DeclareDisconnectPlay(bindings);
        PlayCommonCodecs.DeclarePlayerLoaded(bindings);
        PlayCommonCodecs.DeclarePostEffectsPlay(bindings);
        PlayCommonCodecs.DeclareStoreCookiePlay(bindings);
        PlayCommonCodecs.DeclareTransferPlay(bindings);
        PlayKeepAliveCodecs.DeclareKeepAlivePlay(bindings);
    }
}
