namespace Umpk.Client.Bedrock.Session;

/// <summary>Describes one player-auth-input packet accepted by the transport for sending, with its tick and a copy of the encoded payload.</summary>
/// <remarks>Admission means the transport accepted the send and says nothing about whether the server used the movement.</remarks>
internal readonly record struct BedrockPlayerAuthInputSubmitResult(ulong Tick, byte[] OutboundPayload);
