using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Auth;

namespace Umpk.Realms;

/// <summary>Construction inputs for <see cref="RealmsClient"/>. The credential is required; the HTTP handler factory, base URI, and logger have sensible defaults and are the seams tests override.</summary>
public sealed class RealmsClientOptions
{
    /// <summary>The resolved Realms auth material (from a completed <see cref="JavaSession"/>).</summary>
    public required RealmsSessionCredential Credential { get; init; }

    /// <summary>Supplies the <see cref="HttpMessageHandler"/> every Realms call flows through. Defaults to the shared <see cref="DefaultHttpMessageHandlerFactory"/>; tests inject a scripted handler here.</summary>
    public IHttpMessageHandlerFactory HttpHandlerFactory { get; init; } = DefaultHttpMessageHandlerFactory.Instance;

    /// <summary>The Realms service base URI. Defaults to <c>https://pc.realms.minecraft.net</c>.</summary>
    public Uri BaseUri { get; init; } = new Uri("https://pc.realms.minecraft.net");

    /// <summary>Diagnostic logger. Defaults to a no-op logger. Never receives token material.</summary>
    public ILogger Logger { get; init; } = NullLogger.Instance;
}
