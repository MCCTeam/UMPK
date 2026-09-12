namespace Umpk.Protocol.Java.Packets;

/// <summary>A datapack the client and server agree on (namespace, id, version).</summary>
public sealed record KnownPack(string Namespace, string Id, string Version);
