using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.Session;

internal enum BedrockObservedOakDropDisposition
{
    TransferredWithTentativeRemainder,
    TransferredButSuperseded,
    TransferredButOwnerRetired,
}

/// <summary>Reports actual transport ownership transfer and separate local prediction disposition for the single reviewed internal Drop.</summary>
internal sealed class BedrockObservedOakDropSubmitResult
{
    private readonly byte[] _payload;
    internal BedrockObservedOakDropSubmitResult(byte[] payload, int generation, BedrockContainerAddress address, object authorityReceipt, BedrockObservedOakDropDisposition disposition)
    {
        _payload = (byte[])payload.Clone();
        Generation = generation;
        Address = address;
        AuthorityReceipt = authorityReceipt;
        Disposition = disposition;
    }

    internal int LegacyRequestId => -4;
    internal int Slot => 0;
    internal int Generation { get; }
    internal BedrockContainerAddress Address { get; }
    internal object AuthorityReceipt { get; }
    internal BedrockObservedOakDropDisposition Disposition { get; }
    internal byte[] OutboundPayload => (byte[])_payload.Clone();
}
