using Umpk.Protocol.Java;
using Umpk.Text;

namespace Umpk.Client.Events;

/// <summary>Raised once the play phase begins and the login packet has been applied.</summary>
public sealed record JoinedGame(SessionInfo Session) : IClientEvent;

/// <summary>Raised when the session ends, carrying the reason.</summary>
public sealed record Disconnected(DisconnectInfo Info) : IClientEvent;

/// <summary>Raised when the protocol phase changes.</summary>
public sealed record PhaseChanged(ProtocolPhase Phase) : IClientEvent;

/// <summary>
/// Raised for every decoded inbound packet, carrying the frame identity alongside the packet: the phase it arrived in, its real wire id, and the raw body length in bytes (after the wire id, after decompression). <see cref="WireId"/> was hardcoded to -1 before, so a consumer could name the packet type but never the frame; both fields are now the values read off the wire.
/// <para>This event is inbound and decoded only. Consumers that need the raw bytes, or the outbound direction, subscribe to <see cref="UmpkClient.PacketFrameObserved"/> instead.</para>
/// </summary>
public sealed record PacketReceived(ProtocolPhase Phase, int WireId, object? Packet, int PayloadLength) : IClientEvent;

/// <summary>Raised when the server sends its data / MOTD packet.</summary>
public sealed record ServerDataReceived(Component Motd, bool HasIcon) : IClientEvent;

/// <summary>Raised when the server difficulty is set or changed.</summary>
public sealed record DifficultyChanged(byte Difficulty, bool Locked) : IClientEvent;

/// <summary>Raised when the server asks the client to move to another host (the 1.20.5+ <c>transfer</c> packet, in either the configuration or the play phase). The server closes this connection immediately afterwards; reconnecting to the new endpoint is the consumer's decision. Cookies stored during this session survive in <see cref="UmpkClient"/> and are the mechanism a transfer target uses to recognise the returning client.</summary>
public sealed record ServerTransferRequested(string Host, int Port) : IClientEvent;
