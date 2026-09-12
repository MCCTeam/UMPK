using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Plugins;
using Umpk.Game.Registries;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Signing;
using Umpk.Protocol.Java.Transport;

namespace Umpk.Client;

/// <summary>Fluent builder for a <see cref="UmpkClient"/>. Every seam has a sensible default; only the version and profile are effectively required for a usable session. No statics are touched and nothing is discovered by reflection.</summary>
public sealed class UmpkClientBuilder
{
    private JavaVersion? _version;
    private GameProfile? _profile;
    private ISessionAuthenticator? _authenticator;
    private ProfileCredentials? _credentials;
    private IConnectionFactory? _connectionFactory;
    private IServerAddressResolver? _resolver;
    private ITickSource? _tickSource;
    private ISessionScheduler? _scheduler;
    private ILoggerFactory? _loggerFactory;
    private IBlockShapeSource? _blockShapes;
    private RegistryAccess? _staticRegistries;
    private IChatSigningProvider? _signingProvider;
    private readonly ClientFeatures _features = new();
    private readonly ClientOptions _options = new();
    private readonly ClientPolicies _policies = new();
    private readonly List<IClientPlugin> _plugins = [];

    /// <summary>Uses a fixed protocol version.</summary>
    public UmpkClientBuilder UseVersion(JavaVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        _version = version;
        return this;
    }

    /// <summary>Sets the game profile (username/uuid) to log in as.</summary>
    public UmpkClientBuilder UseProfile(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile;
        return this;
    }

    /// <summary>Uses an online-mode session authenticator plus the profile credentials.</summary>
    public UmpkClientBuilder UseAuthenticator(ISessionAuthenticator authenticator, ProfileCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(credentials);
        _authenticator = authenticator;
        _credentials = credentials;
        return this;
    }

    /// <summary>Uses a custom connection factory (proxies, in-memory pipes).</summary>
    public UmpkClientBuilder UseConnectionFactory(IConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _connectionFactory = factory;
        return this;
    }

    /// <summary>Uses a custom server address resolver.</summary>
    public UmpkClientBuilder UseAddressResolver(IServerAddressResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
        return this;
    }

    /// <summary>Uses a host-driven tick source.</summary>
    public UmpkClientBuilder UseTickSource(ITickSource ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        _tickSource = ticks;
        return this;
    }

    /// <summary>Uses a host-driven session scheduler.</summary>
    public UmpkClientBuilder UseScheduler(ISessionScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
        return this;
    }

    /// <summary>Uses a host logger factory (default: <see cref="NullLoggerFactory"/>).</summary>
    public UmpkClientBuilder UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>Overrides the block-shape source physics and pathfinding collide against. Optional: a session with no override uses the version's generated shape tables (<c>Umpk.Data.Java.JavaGameData.BlockShapes</c>), which already carry vanilla slab, stair, fence, wall, pane and carpet geometry. Supply one to model a modded server's blocks.</summary>
    public UmpkClientBuilder UseBlockShapes(IBlockShapeSource shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        _blockShapes = shapes;
        return this;
    }

    /// <summary>Supplies the version's static registries (item registry etc.) to install into the codec context at the transition into Play, so clientbound item stacks resolve their ids. Composition roots build this from the version data (see <c>Umpk.Data.Java.JavaGameData.Registries</c>); without it the session cannot decode non-air items and drops on the first item packet under the strict decode policy.</summary>
    public UmpkClientBuilder UseStaticRegistries(RegistryAccess registries)
    {
        ArgumentNullException.ThrowIfNull(registries);
        _staticRegistries = registries;
        return this;
    }

    /// <summary>Enables chat signing by supplying the player certificate source. On signing-era versions the client constructs the per-session <see cref="Umpk.Protocol.Java.Signing.ChatSigningSession"/> and derives the <see cref="Umpk.Protocol.Java.Signing.ChatSignatureEra"/> from the negotiated version, resolving certificates from <paramref name="provider"/> and refreshing them on expiry. Without this seam (or on a <c>none</c> signing-era version) the send path stays unsigned, byte-identical to the offline path.</summary>
    public UmpkClientBuilder UseChatSigning(IChatSigningProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _signingProvider = provider;
        return this;
    }

    /// <summary>Configures the feature composition.</summary>
    public UmpkClientBuilder ConfigureFeatures(Action<ClientFeatures> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_features);
        return this;
    }

    /// <summary>Configures the bindable options.</summary>
    public UmpkClientBuilder ConfigureOptions(Action<ClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_options);
        return this;
    }

    /// <summary>Configures the code-defined policies (reconnect, resource pack).</summary>
    public UmpkClientBuilder ConfigurePolicies(Action<ClientPolicies> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_policies);
        return this;
    }

    /// <summary>Adds a plugin.</summary>
    public UmpkClientBuilder AddPlugin(IClientPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _plugins.Add(plugin);
        return this;
    }

    /// <summary>Builds the client.</summary>
    public UmpkClient Build()
    {
        JavaVersion version = _version
            ?? throw new InvalidOperationException("A version is required; call UseVersion.");
        GameProfile profile = _profile
            ?? throw new InvalidOperationException("A profile is required; call UseProfile.");

        var settings = new UmpkClientSettings
        {
            Version = version,
            Profile = profile,
            Authenticator = _authenticator,
            Credentials = _credentials,
            ConnectionFactory = _connectionFactory ?? new TcpConnectionFactory(),
            Resolver = _resolver ?? DnsSrvResolver.Passthrough,
            TickSource = _tickSource,
            Scheduler = _scheduler,
            LoggerFactory = _loggerFactory ?? NullLoggerFactory.Instance,
            BlockShapes = _blockShapes,
            StaticRegistries = _staticRegistries,
            SigningProvider = _signingProvider,
            Features = _features.Normalized(),
            Options = _options,
            Policies = _policies,
            Plugins = [.. _plugins],
        };

        return new UmpkClient(settings);
    }

    /// <summary>Builds the client, auto-detecting the protocol version from a status ping when <see cref="UseVersion"/> was never called. With an explicit version already set this does no I/O at all and is exactly <see cref="Build"/>.</summary>
    /// <param name="endpoint">The server to ping when no version was set.</param>
    /// <param name="ct">Cancels the status ping.</param>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is null.</exception>
    /// <exception cref="InvalidOperationException">No profile was set; call <see cref="UseProfile"/>.</exception>
    /// <exception cref="VersionResolutionException">No version was set and the status ping could not resolve one; see the exception's <see cref="VersionResolutionException.Failure"/> for why.</exception>
    public async Task<UmpkClient> BuildForAsync(ServerEndpoint endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (_version is not null)
            return Build();

        ILoggerFactory loggerFactory = _loggerFactory ?? NullLoggerFactory.Instance;
        var negotiator = new ServerVersionNegotiator(loggerFactory);
        var options = new JavaStatusOptions
        {
            Timeout = _options.StatusPingTimeout,
            Logger = loggerFactory.CreateLogger("Umpk.Client.ServerVersionNegotiator"),
        };
        IConnectionFactory factory = _connectionFactory ?? new TcpConnectionFactory();
        IServerAddressResolver resolver = _resolver ?? DnsSrvResolver.Passthrough;

        VersionNegotiation negotiation = await negotiator
            .DetectAsync(endpoint, options, ct, resolver, factory)
            .ConfigureAwait(false);

        if (negotiation.Version is null)
            throw new VersionResolutionException(
                $"Could not determine the protocol version {endpoint} speaks ({negotiation.Failure}).",
                endpoint.Host, endpoint.Port, negotiation.Status?.Protocol, negotiation.Failure, negotiation.Fault);

        UseVersion(negotiation.Version);
        return Build();
    }
}

/// <summary>The resolved, immutable construction inputs for a <see cref="UmpkClient"/>.</summary>
internal sealed record UmpkClientSettings
{
    public required JavaVersion Version { get; init; }

    public required GameProfile Profile { get; init; }

    public ISessionAuthenticator? Authenticator { get; init; }

    public ProfileCredentials? Credentials { get; init; }

    public required IConnectionFactory ConnectionFactory { get; init; }

    public required IServerAddressResolver Resolver { get; init; }

    public ITickSource? TickSource { get; init; }

    public ISessionScheduler? Scheduler { get; init; }

    public required ILoggerFactory LoggerFactory { get; init; }

    public IBlockShapeSource? BlockShapes { get; init; }

    public RegistryAccess? StaticRegistries { get; init; }

    public IChatSigningProvider? SigningProvider { get; init; }

    public required ClientFeatures Features { get; init; }

    public required ClientOptions Options { get; init; }

    public required ClientPolicies Policies { get; init; }

    public required IReadOnlyList<IClientPlugin> Plugins { get; init; }
}
