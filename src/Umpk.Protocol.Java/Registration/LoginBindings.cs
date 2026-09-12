using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>Login-phase timelines: authentication handshake, encryption, compression, and the login/config-shared cookie and custom-query round trips.</summary>
internal static class LoginBindings
{
    /// <summary>Adds this family's packet timelines to the binding table.</summary>
    public static void Register(PacketBindings bindings)
    {
        LoginChannelCodecs.DeclareCookieRequestLogin(bindings);
        LoginChannelCodecs.DeclareCookieResponseLogin(bindings);
        LoginChannelCodecs.DeclareCustomQuery(bindings);
        LoginChannelCodecs.DeclareCustomQueryAnswer(bindings);
        LoginCodecs.DeclareHello(bindings);
        LoginCodecs.DeclareKey(bindings);
        LoginCodecs.DeclareLoginAcknowledged(bindings);
        LoginCodecs.DeclareLoginCompression(bindings);
        LoginCodecs.DeclareLoginDisconnect(bindings);
        LoginCodecs.DeclareLoginFinished(bindings);
    }
}
