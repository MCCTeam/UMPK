namespace Umpk.Client.Bedrock.Actions;

/// <summary>Identifies whether a claimed action identifier travels in a standalone client request or embedded in a replay batch.</summary>
internal enum BedrockActionEmission
{
    /// <summary>Marks the single outstanding client inventory request identifier claimed for a standalone item-stack request.</summary>
    Standalone,
    /// <summary>Marks action identifiers claimed together as one atomic replay batch.</summary>
    Embedded,
}

/// <summary>Tracks action identifiers claimed by standalone requests or embedded replay batches.</summary>
/// <remarks>The session currently admits one outstanding item-stack request and pairs its claim with a response-tracker registration; this coordinator also supports bounded replay batches.</remarks>
internal sealed class BedrockActionCoordinator
{
    /// <summary>Bounds how many action identifiers may stay claimed at once so a leaked claim cannot grow the claim table without limit.</summary>
    internal const int MaxClaims = 256;

    private readonly Lock _gate = new();
    private readonly Dictionary<int, BedrockActionEmission> _claims = new();

    /// <summary>Gets how many action identifiers are currently claimed.</summary>
    internal int ClaimedCount
    {
        get
        {
            lock (_gate)
                return _claims.Count;
        }
    }

    /// <summary>Claims one action identifier unless it is already claimed or the claim table is full.</summary>
    internal bool TryClaim(int actionId, BedrockActionEmission emission)
    {
        lock (_gate)
        {
            if (_claims.ContainsKey(actionId))
                return false;
            if (_claims.Count >= MaxClaims)
                return false;
            _claims[actionId] = emission;
            return true;
        }
    }

    /// <summary>Reports whether an action identifier is currently claimed.</summary>
    internal bool IsClaimed(int actionId)
    {
        lock (_gate)
            return _claims.ContainsKey(actionId);
    }

    /// <summary>Releases one claimed action identifier so its request identifier can be admitted again.</summary>
    internal void Release(int actionId)
    {
        lock (_gate)
            _claims.Remove(actionId);
    }

    /// <summary>Claims a batch of action identifiers as one atomic embedded replay or claims none of them.</summary>
    internal bool TryBeginReplay(IReadOnlyList<int> actionIds)
    {
        ArgumentNullException.ThrowIfNull(actionIds);
        lock (_gate)
        {
            foreach (int actionId in actionIds)
            {
                if (_claims.ContainsKey(actionId))
                    return false;
            }

            if (_claims.Count + actionIds.Count > MaxClaims)
                return false;
            foreach (int actionId in actionIds)
                _claims[actionId] = BedrockActionEmission.Embedded;
            return true;
        }
    }

    /// <summary>Drops every claim after a lifecycle boundary so no stale request identifier stays admitted.</summary>
    internal void Invalidate()
    {
        lock (_gate)
            _claims.Clear();
    }
}
