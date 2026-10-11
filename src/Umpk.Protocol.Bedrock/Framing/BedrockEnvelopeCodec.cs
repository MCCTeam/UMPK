namespace Umpk.Protocol.Bedrock;

internal static class BedrockEnvelopeCodec
{
    internal const byte Marker = 0xfe;

    internal static byte[] Wrap(ReadOnlySpan<byte> payload)
    {
        byte[] envelope = new byte[payload.Length + 1];
        envelope[0] = Marker;
        payload.CopyTo(envelope.AsSpan(1));
        return envelope;
    }

    internal static ReadOnlySpan<byte> Unwrap(ReadOnlySpan<byte> envelope)
    {
        if (envelope.IsEmpty || envelope[0] != Marker)
            throw new BedrockCodecException("The transport message is not a Bedrock batch envelope.");

        return envelope[1..];
    }
}
