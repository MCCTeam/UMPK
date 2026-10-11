using System.Net;
using Microsoft.Extensions.Logging;

namespace Umpk.Transport.RakNet;

internal static partial class RakNetLog
{
    [LoggerMessage(4100, LogLevel.Debug, "RakNet diagnostic: {Message}")]
    public static partial void ForkDiagnostic(ILogger logger, string message);

    [LoggerMessage(4101, LogLevel.Error, "RakNet dial to {Endpoint} failed")]
    public static partial void DialFailed(ILogger logger, IPEndPoint endpoint, Exception exception);

    [LoggerMessage(4102, LogLevel.Warning, "RakNet outbound admission exceeded configured capacity")]
    public static partial void AdmissionExceeded(ILogger logger);

    [LoggerMessage(4103, LogLevel.Debug, "RakNet connection ended with {Reason}")]
    public static partial void ConnectionEnded(ILogger logger, global::RakNet.RakNetTerminationReason reason);
}
