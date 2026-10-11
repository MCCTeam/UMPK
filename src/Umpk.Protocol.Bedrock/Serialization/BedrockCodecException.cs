namespace Umpk.Protocol.Bedrock;

internal sealed class BedrockCodecException : Exception
{
    internal BedrockCodecException(string message)
        : base(message)
    {
    }

    internal BedrockCodecException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
