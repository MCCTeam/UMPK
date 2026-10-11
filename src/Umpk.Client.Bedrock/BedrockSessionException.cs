using Umpk.Client.Bedrock.Session;

namespace Umpk.Client.Bedrock;

internal sealed class BedrockSessionException(BedrockTermination termination)
    : Exception(termination?.Detail ?? $"The Bedrock session ended with {termination?.Reason}.")
{
    internal BedrockTermination Termination { get; } = termination ?? throw new ArgumentNullException(nameof(termination));
}
