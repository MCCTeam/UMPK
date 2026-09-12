using Umpk.Protocol.Java;

namespace Umpk.Client;

/// <summary>Thrown by a version-optional client action when the negotiated protocol version cannot put the action's packet on the wire: the version never had it, the version carries it only as a registered-but-unimplemented marker, or the version binds a DIFFERENT packet record under the same identifier than the one this action constructs.</summary>
/// <remarks>
/// <para>To branch instead of catching, ask <see cref="ClientActionCapabilities"/> first (<c>client.Capabilities</c> / <c>client.Actions.Capabilities</c>). It answers the same question with the same predicate, so a true answer there means the matching send will not throw this.</para>
/// </remarks>
public sealed class ActionNotSupportedException : NotSupportedException
{
    /// <summary>Creates the exception for an action whose packet the version cannot carry.</summary>
    /// <param name="action">The action surface member that could not send (for example <c>RenameItemAsync</c>).</param>
    /// <param name="packet">The packet identity the action would have sent.</param>
    /// <param name="protocol">The negotiated wire protocol number.</param>
    /// <param name="detail">An optional extra sentence appended to the message (for example a pointer to the sibling method).</param>
    public ActionNotSupportedException(string action, Identifier packet, int protocol, string? detail = null)
        : base(BuildMessage(action, packet, protocol, detail))
    {
        Action = action;
        Packet = packet;
        Protocol = protocol;
    }

    /// <summary>The action surface member that could not send.</summary>
    public string Action { get; }

    /// <summary>The packet identity the action would have sent.</summary>
    public Identifier Packet { get; }

    /// <summary>The negotiated wire protocol number the action was attempted on.</summary>
    public int Protocol { get; }

    private static string BuildMessage(string action, Identifier packet, int protocol, string? detail)
    {
        string head = $"{action} cannot be sent on protocol {protocol}: this version has no sendable {packet}.";
        return string.IsNullOrEmpty(detail) ? head : head + " " + detail;
    }
}
