namespace Umpk.Realms;

/// <summary>A client for the Java Realms REST service (<c>pc.realms.minecraft.net</c>). All calls authenticate with the resolved session material supplied at construction and flow through an injected HTTP handler. Failures surface as <see cref="RealmsException"/>. Every call accepts a <see cref="CancellationToken"/>. Dispose the client to release the underlying HTTP client.</summary>
public interface IRealmsClient : IDisposable
{
    /// <summary>Checks whether the reported client version is accepted by Realms (<c>GET /mco/client/compatible</c>).</summary>
    Task<RealmsCompatibility> CheckClientCompatibleAsync(CancellationToken ct);

    /// <summary>Acknowledges the Realms terms of service (<c>POST /mco/tos/agreed</c>). Required once per account before worlds can be listed or joined.</summary>
    Task AgreeToTermsAsync(CancellationToken ct);

    /// <summary>Lists the Realms worlds available to the authenticated player (<c>GET /worlds</c>).</summary>
    Task<IReadOnlyList<RealmWorld>> ListWorldsAsync(CancellationToken ct);

    /// <summary>Resolves the connection address for a Realms world by id (<c>GET /worlds/v1/{id}/join/pc</c>).</summary>
    Task<RealmServerAddress> JoinWorldAsync(long worldId, CancellationToken ct);
}
