namespace Umpk.Protocol.Java;

/// <summary>One configuration-phase packet handed to <see cref="JavaLoginOptions.ConfigurationPacketObserver"/>, with the frame identity the host needs to report it the same way it reports play-phase packets. The driver owns these frames, so the observation includes the wire id and payload length that are not recoverable from the decoded packet object.</summary>
/// <param name="Packet">The decoded packet object.</param>
/// <param name="WireId">The frame's wire id in the configuration phase.</param>
/// <param name="PayloadLength">The frame body length in bytes, after the wire id and after decompression.</param>
public readonly record struct ObservedConfigurationPacket(object Packet, int WireId, int PayloadLength);
