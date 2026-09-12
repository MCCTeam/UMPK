using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>The phase-gate table: the terminal packets that end a phase and the frames after which the byte pipeline arms compression or encryption. Applied for every registered packet, implemented or marker, exactly once at descriptor construction. Keyed by <c>(phase, flow, identifier)</c>; the read loop parks at each of these frame boundaries until the consumer acknowledges the transition, which is why the exact set is load-bearing rather than cosmetic.</summary>
internal static class ProtocolGates
{
    private static readonly Dictionary<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id), ProtocolPhase> Terminals = BuildTerminals();

    private static readonly HashSet<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id)> CompressionEnablePoints = BuildCompressionEnablePoints();

    private static readonly HashSet<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id)> EncryptionEnablePoints = BuildEncryptionEnablePoints();

    /// <summary>Applies any terminal / compression / encryption gate that matches this packet.</summary>
    public static void Apply(ProtocolDescriptorBuilder builder, ProtocolPhase phase, PacketFlow flow, int wireId, Identifier id)
    {
        (ProtocolPhase, PacketFlow, Identifier) key = (phase, flow, id);
        if (Terminals.TryGetValue(key, out ProtocolPhase next))
            builder.MarkTerminal(phase, flow, wireId, next);

        if (CompressionEnablePoints.Contains(key))
            builder.MarkCompressionEnablePoint(phase, flow, wireId);

        if (EncryptionEnablePoints.Contains(key))
            builder.MarkEncryptionEnablePoint(phase, flow, wireId);

    }

    private static Dictionary<(ProtocolPhase, PacketFlow, Identifier), ProtocolPhase> BuildTerminals() => new()
    {
        // Handshake intention (serverbound) -> login: the server read loop must park after decoding the handshake so the consumer acknowledges the phase before the next frame (login-start, also wire id 0) is read; without this gate the login-start would be decoded as a handshake. The nextState field is dynamic (2 = login, 3 = transfer), but both enter the login flow, and the status path never uses this driver, so marking the login transition here is correct for the login/transfer handshakes and harmless to the client role (which sends, never decodes, the handshake).
        [(ProtocolPhase.Handshake, PacketFlow.Serverbound, HandshakePackets.Serverbound.Intention.Id)] = ProtocolPhase.Login,

        // On pre-1.20.2 versions login-finished (clientbound) goes straight to play; the config-phase versions instead transition on login-acknowledged (serverbound), so the login-finished terminal is only load-bearing on 1.8. finish-configuration (both flows) ends the configuration phase.
        [(ProtocolPhase.Login, PacketFlow.Serverbound, LoginPackets.Serverbound.LoginAcknowledged.Id)] = ProtocolPhase.Configuration,
        [(ProtocolPhase.Login, PacketFlow.Clientbound, LoginPackets.Clientbound.LoginFinished.Id)] = ProtocolPhase.Play,
        [(ProtocolPhase.Configuration, PacketFlow.Clientbound, ConfigurationPackets.Clientbound.FinishConfiguration.Id)] = ProtocolPhase.Play,
        [(ProtocolPhase.Configuration, PacketFlow.Serverbound, ConfigurationPackets.Serverbound.FinishConfiguration.Id)] = ProtocolPhase.Play,

        // The Play-to-Configuration re-entry (1.20.2+): the clientbound start-configuration parks the client read loop until the consumer acknowledges and swaps to the configuration descriptor. The serverbound acknowledgment is the server-side mirror and changes the phase only when received.
        [(ProtocolPhase.Play, PacketFlow.Clientbound, PlayPackets.Clientbound.StartConfiguration.Id)] = ProtocolPhase.Configuration,
        [(ProtocolPhase.Play, PacketFlow.Serverbound, PlayPackets.Serverbound.ConfigurationAcknowledged.Id)] = ProtocolPhase.Configuration,
    };

    private static HashSet<(ProtocolPhase, PacketFlow, Identifier)> BuildCompressionEnablePoints() =>
    [
        // Set-compression (clientbound login): after this frame every subsequent frame uses the compressed wire format. The read loop must pause here until the consumer enables compression, so the next frame is read with the compressed reader (the transform-enable point). This is the mirror of the terminal-packet phase gate, but for a byte-pipeline transform rather than a phase change.
        (ProtocolPhase.Login, PacketFlow.Clientbound, LoginPackets.Clientbound.LoginCompression.Id),
    ];

    private static HashSet<(ProtocolPhase, PacketFlow, Identifier)> BuildEncryptionEnablePoints() =>
    [
        // Encryption-request boundary. Client role: the clientbound login hello (encryption request) is the frame after which the client sends its key response and enables encryption. Server role: the serverbound key response is the frame after which the server enables encryption. In both cases the read loop must park here until the consumer calls EnableEncryption, so the peer's subsequent encrypted bytes are not appended to the read buffer undecrypted. The server also enables encryption immediately after validating the key and before reading another frame.
        (ProtocolPhase.Login, PacketFlow.Clientbound, LoginPackets.Clientbound.Hello.Id),
        (ProtocolPhase.Login, PacketFlow.Serverbound, LoginPackets.Serverbound.Key.Id),
    ];
}
