namespace Umpk.Client.Bedrock;

internal sealed record BedrockClientTimeouts(
    TimeSpan Dial,
    TimeSpan NetworkSettings,
    TimeSpan Credentials,
    TimeSpan LoginSuccess,
    TimeSpan ResourceNegotiation,
    TimeSpan Ready,
    TimeSpan CloseDrain)
{
    internal static BedrockClientTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(5));

    internal void Validate()
    {
        if (Dial <= TimeSpan.Zero || NetworkSettings <= TimeSpan.Zero || Credentials <= TimeSpan.Zero
            || LoginSuccess <= TimeSpan.Zero || ResourceNegotiation <= TimeSpan.Zero
            || Ready <= TimeSpan.Zero || CloseDrain < TimeSpan.Zero)
            throw new ArgumentException("Bedrock client timeouts must be positive, with a non-negative close drain.");
    }
}
