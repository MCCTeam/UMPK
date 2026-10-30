namespace Umpk.Protocol.Java.Codecs;

/// <summary>A modeled wire value whose complete native DATA hash is unavailable. This must propagate through containing components; only the top-level hashed-stack owner may omit the entire unresolved component for server resynchronization.</summary>
internal sealed class ComponentHashUnavailableException(string message) : InvalidOperationException(message);
