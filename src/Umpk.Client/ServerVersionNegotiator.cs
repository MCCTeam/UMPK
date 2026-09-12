using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;

namespace Umpk.Client;

/// <summary>Resolves which protocol version a server speaks from a status ping: ping, read <c>version.protocol</c>, look it up in the <see cref="JavaVersions"/> catalog. A failed detection is a normal, host-recoverable outcome (a host can prompt the player for a version, or retry), so this returns a <see cref="VersionNegotiation"/> result rather than throwing; the exception belongs one layer up, at <see cref="UmpkClientBuilder.BuildForAsync"/>.</summary>
public sealed class ServerVersionNegotiator
{
    private static readonly Regex VersionTokenRegex = new(@"\d+\.\d+(?:\.\d+)?", RegexOptions.Compiled);

    private readonly ILogger _logger;

    /// <summary>Creates a negotiator, optionally logging status-ping diagnostics through <paramref name="loggerFactory"/>.</summary>
    public ServerVersionNegotiator(ILoggerFactory? loggerFactory = null)
    {
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("Umpk.Client.ServerVersionNegotiator");
    }

    /// <summary>Pings <paramref name="endpoint"/> and resolves the protocol version its status reports.</summary>
    /// <param name="endpoint">The server to ping.</param>
    /// <param name="options">Status-ping options. Defaults to a fresh <see cref="JavaStatusOptions"/> using this negotiator's logger when null.</param>
    /// <param name="ct">Cancels the ping.</param>
    /// <param name="resolver">The address resolver, forwarded to <see cref="JavaStatus.QueryAsync(string, ushort, JavaStatusOptions, CancellationToken, IServerAddressResolver?, IConnectionFactory?)"/>. Null falls back to that overload's own default.</param>
    /// <param name="factory">The connection factory. Null falls back to that overload's own default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is null.</exception>
    public async Task<VersionNegotiation> DetectAsync(
        ServerEndpoint endpoint, JavaStatusOptions? options = null, CancellationToken ct = default,
        IServerAddressResolver? resolver = null, IConnectionFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        options ??= new JavaStatusOptions { Logger = _logger };

        ServerStatus status;
        try
        {
            status = await JavaStatus
                .QueryAsync(endpoint.Host, endpoint.Port, options, ct, resolver, factory)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new VersionNegotiation
            {
                Failure = VersionNegotiationFailure.StatusPingFailed,
                Fault = ex,
            };
        }

        if (status.Protocol is not int protocol)
        {
            if (TryResolveViaVersionName(status.VersionName, -1, out JavaVersion minedNoProto))
            {
                _logger.LogDebug(
                    "Version auto-detection: no protocol field, mined {Version} ({Proto}) from version.name '{Name}'.",
                    minedNoProto.Version.Name, minedNoProto.Version.Protocol, status.VersionName);
                return new VersionNegotiation { Status = status, Version = minedNoProto };
            }

            if (TryResolveViaHighest(out JavaVersion highestNoProto))
            {
                _logger.LogInformation(
                    "Version auto-detection: no protocol field (name '{Name}'), defaulting to highest supported {Version} ({Proto}).",
                    status.VersionName ?? "(null)", highestNoProto.Version.Name, highestNoProto.Version.Protocol);
                return new VersionNegotiation { Status = status, Version = highestNoProto };
            }

            return new VersionNegotiation
            {
                Status = status,
                Failure = VersionNegotiationFailure.NoProtocol,
            };
        }

        int normalized = NormalizeSnapshotProtocol(protocol);
        if (normalized != protocol && JavaVersions.TryGetByProtocol(normalized, out JavaVersion snapshotMapped))
        {
            _logger.LogDebug(
                "Version auto-detection: snapshot protocol {Raw} normalized to {Norm} ({Version}).",
                protocol, normalized, snapshotMapped.Version.Name);
            return new VersionNegotiation { Status = status, Version = snapshotMapped };
        }

        protocol = normalized;

        if (JavaVersions.TryGetByProtocol(protocol, out JavaVersion version))
            return new VersionNegotiation
            {
                Status = status,
                Version = version,
            };

        if (TryResolveViaVersionName(status.VersionName, protocol, out JavaVersion mined))
        {
            _logger.LogInformation(
                "Version auto-detection: unsupported protocol {Reported} ('{Name}') upgraded via version.name to {Version} ({Proto}).",
                protocol, status.VersionName, mined.Version.Name, mined.Version.Protocol);
            return new VersionNegotiation { Status = status, Version = mined };
        }

        if (protocol == -1)
        {
            if (TryResolveViaHighest(out JavaVersion highest))
            {
                _logger.LogInformation(
                    "Version auto-detection: unsupported sentinel protocol -1 (name '{Name}'), defaulting to highest {Version} ({Proto}).",
                    status.VersionName ?? "(null)", highest.Version.Name, highest.Version.Protocol);
                return new VersionNegotiation { Status = status, Version = highest };
            }
        }
        else if (TryResolveViaClosest(protocol, out JavaVersion closest))
        {
            _logger.LogInformation(
                "Version auto-detection: unsupported protocol {Reported} (name '{Name}'), falling back to closest supported {Version} ({Proto}).",
                protocol, status.VersionName ?? "(null)", closest.Version.Name, closest.Version.Protocol);
            return new VersionNegotiation { Status = status, Version = closest };
        }
        else if (TryResolveViaHighest(out JavaVersion fallbackHighest))
        {
            _logger.LogInformation(
                "Version auto-detection: unsupported protocol {Reported}, no closest found, defaulting to highest {Version} ({Proto}).",
                protocol, fallbackHighest.Version.Name, fallbackHighest.Version.Protocol);
            return new VersionNegotiation { Status = status, Version = fallbackHighest };
        }

        return new VersionNegotiation
        {
            Status = status,
            Failure = VersionNegotiationFailure.UnsupportedProtocol,
        };
    }

    private static bool TryResolveViaVersionName(string? versionName, int reportedProtocol, out JavaVersion best)
    {
        best = default!;
        if (string.IsNullOrEmpty(versionName))
            return false;

        System.Text.RegularExpressions.MatchCollection matches = VersionTokenRegex.Matches(versionName);
        if (matches.Count < 2)
            return false;

        int bestProtocol = reportedProtocol;
        JavaVersion? bestVersion = null;

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            string token = m.Value;
            if (!JavaVersions.TryGetByName(token, out JavaVersion candidate))
                continue;
            int proto = candidate.Version.Protocol;
            if (proto > bestProtocol)
            {
                bestProtocol = proto;
                bestVersion = candidate;
            }
        }

        if (bestVersion is not null && bestProtocol > reportedProtocol)
        {
            best = bestVersion;
            return true;
        }

        return false;
    }

    private static bool TryResolveViaClosest(int reportedProtocol, out JavaVersion closest)
    {
        closest = default!;
        IReadOnlyList<JavaVersion> all = JavaVersions.All;
        if (all.Count == 0)
            return false;

        JavaVersion? best = null;
        int bestDistance = int.MaxValue;

        foreach (JavaVersion candidate in all)
        {
            int proto = candidate.Version.Protocol;
            int distance = Math.Abs(proto - reportedProtocol);
            if (best is null || distance < bestDistance || (distance == bestDistance && proto > best!.Version.Protocol))
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best is not null)
        {
            closest = best;
            return true;
        }

        return false;
    }

    private static bool TryResolveViaHighest(out JavaVersion highest)
    {
        highest = default!;
        IReadOnlyList<JavaVersion> all = JavaVersions.All;
        if (all.Count == 0)
            return false;

        JavaVersion? best = null;
        foreach (JavaVersion candidate in all)
            if (best is null || candidate.Version.Protocol > best.Version.Protocol)
                best = candidate;

        if (best is not null)
        {
            highest = best;
            return true;
        }

        return false;
    }

    private static int NormalizeSnapshotProtocol(int protocol)
    {
        if ((protocol & 0x40000000) == 0)
            return protocol;

        return protocol switch
        {
            0x4000012E => 775, // 26.1-rc-2 -> 26.1
            _ => protocol,
        };
    }
}

/// <summary>The outcome of a <see cref="ServerVersionNegotiator.DetectAsync"/> call.</summary>
public sealed class VersionNegotiation
{
    /// <summary>The resolved version, or null when detection failed.</summary>
    public JavaVersion? Version { get; init; }

    /// <summary>The status the ping produced, or null when the ping itself failed (<see cref="Failure"/> is <see cref="VersionNegotiationFailure.StatusPingFailed"/>).</summary>
    public ServerStatus? Status { get; init; }

    /// <summary>Why detection failed, or <see cref="VersionNegotiationFailure.None"/> on success.</summary>
    public VersionNegotiationFailure Failure { get; init; }

    /// <summary>The underlying fault when <see cref="Failure"/> is <see cref="VersionNegotiationFailure.StatusPingFailed"/>; null otherwise.</summary>
    public Exception? Fault { get; init; }

    /// <summary>True when <see cref="Version"/> was resolved.</summary>
    public bool Succeeded => Version is not null;
}

/// <summary>Why <see cref="ServerVersionNegotiator.DetectAsync"/> could not resolve a version.</summary>
public enum VersionNegotiationFailure
{
    /// <summary>Detection succeeded; see <see cref="VersionNegotiation.Version"/>.</summary>
    None,

    /// <summary>The status ping itself failed (socket fault, timeout, protocol violation, closed connection). See <see cref="VersionNegotiation.Fault"/>.</summary>
    StatusPingFailed,

    /// <summary>The status answered but carried no usable <c>version.protocol</c> field.</summary>
    NoProtocol,

    /// <summary>The reported protocol number is not one UMPK has data for.</summary>
    UnsupportedProtocol,
}
