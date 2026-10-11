using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Classifies how one protocol-2169 item-stack response resolves against the registrations of the current generation.</summary>
internal enum BedrockItemStackResponseOutcome
{
    /// <summary>Marks a response whose request was registered in the current generation and accepted by the server.</summary>
    Accepted,
    /// <summary>Marks a response whose request was registered in the current generation and rejected by the server.</summary>
    Rejected,
    /// <summary>Marks a response with no matching registration in the current generation and no completed record.</summary>
    UnknownRequest,
    /// <summary>Marks a repeat response for a request already resolved in the current generation or earlier in the same batch.</summary>
    DuplicateResponse,
    /// <summary>Marks a response that arrived under a superseded generation and therefore mutates no state.</summary>
    StaleGeneration,
}

/// <summary>Carries one authoritative baseline slot written only from an accepted protocol-2169 item-stack response.</summary>
internal readonly record struct BedrockItemStackBaselineSlot(byte ContainerId, uint? DynamicContainerId, byte Slot, byte Count, int? StackId, string CustomName, string FilteredCustomName, int DurabilityCorrection);

/// <summary>Registers client inventory requests under the current generation and reconciles each protocol-2169 response batch against those registrations.</summary>
/// <remarks>Generation-gated registration is client support policy over the protocol-2169 ItemStackResponse facts: per-batch duplicates collapse to one outcome and only accepted responses refresh the authoritative baseline.</remarks>
internal sealed class BedrockItemStackRequestTracker
{
    /// <summary>Bounds how many requests may wait for a server response before further admission is refused.</summary>
    internal const int MaxPendingRequests = 64;
    /// <summary>Bounds how many resolved request identifiers are remembered for duplicate detection.</summary>
    internal const int MaxCompletedRequests = 256;
    /// <summary>Bounds how many authoritative baseline slots an accepted response batch may establish.</summary>
    internal const int MaxBaselineEntries = 4096;

    private readonly Lock _gate = new();
    private readonly Dictionary<int, int> _pending = new();
    private readonly Dictionary<int, int> _completed = new();
    private readonly Queue<int> _completedOrder = new();
    private readonly Dictionary<BaselineKey, BedrockItemStackBaselineSlot> _baseline = new();
    private int _generation = 1;
    private int _acceptedCount;
    private int _rejectedCount;
    private int _unknownCount;
    private int _duplicateCount;
    private int _staleCount;

    /// <summary>Gets the current registration generation, advanced by every invalidation so stale work can be recognized.</summary>
    internal int Generation
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    /// <summary>Gets how many requests are registered and still waiting for a server response.</summary>
    internal int PendingCount
    {
        get
        {
            lock (_gate)
                return _pending.Count;
        }
    }

    /// <summary>Gets how many registered requests the server has accepted.</summary>
    internal int AcceptedCount
    {
        get
        {
            lock (_gate)
                return _acceptedCount;
        }
    }

    /// <summary>Gets how many registered requests the server has rejected.</summary>
    internal int RejectedCount
    {
        get
        {
            lock (_gate)
                return _rejectedCount;
        }
    }

    /// <summary>Gets how many responses arrived with no matching registration or completed record.</summary>
    internal int UnknownCount
    {
        get
        {
            lock (_gate)
                return _unknownCount;
        }
    }

    /// <summary>Gets how many responses repeated an already resolved request.</summary>
    internal int DuplicateCount
    {
        get
        {
            lock (_gate)
                return _duplicateCount;
        }
    }

    /// <summary>Gets how many responses arrived under a superseded generation without mutating state.</summary>
    internal int StaleCount
    {
        get
        {
            lock (_gate)
                return _staleCount;
        }
    }

    /// <summary>Gets how many authoritative baseline slots were established by accepted responses.</summary>
    internal int BaselineCount
    {
        get
        {
            lock (_gate)
                return _baseline.Count;
        }
    }

    /// <summary>Registers one request identifier under the current generation unless it is stale, duplicated, or the pending table is full.</summary>
    internal bool TryRegister(int requestId, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return false;
            if (_pending.ContainsKey(requestId))
                return false;
            if (_pending.Count >= MaxPendingRequests)
                return false;
            _pending[requestId] = generation;
            return true;
        }
    }

    /// <summary>Cancels one pending registration under the current generation so a failed admission leaves no orphan behind.</summary>
    internal bool TryCancel(int requestId, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return false;
            return _pending.Remove(requestId);
        }
    }

    /// <summary>Applies one protocol-2169 response under the current generation and returns how it resolved.</summary>
    internal BedrockItemStackResponseOutcome ApplyResponse(BedrockItemStackResponse response, int generation)
    {
        ArgumentNullException.ThrowIfNull(response);
        return ApplyResponses([response], generation)[0];
    }

    /// <summary>Applies one response batch under the current generation, collapsing per-batch duplicates and updating the authoritative baseline only for accepted responses.</summary>
    /// <remarks>A stale generation increments the stale-response count without changing pending requests or baseline slots. Invalidate drops the baseline and advances the generation; ClearPending keeps both.</remarks>
    internal IReadOnlyList<BedrockItemStackResponseOutcome> ApplyResponses(IReadOnlyList<BedrockItemStackResponse> responses, int generation)
    {
        ArgumentNullException.ThrowIfNull(responses);
        lock (_gate)
        {
            var outcomes = new BedrockItemStackResponseOutcome[responses.Count];
            if (generation != _generation)
            {
                for (int index = 0; index < outcomes.Length; index++)
                {
                    ArgumentNullException.ThrowIfNull(responses[index]);
                    _staleCount++;
                    outcomes[index] = BedrockItemStackResponseOutcome.StaleGeneration;
                }

                return outcomes;
            }

            var firstOutcomeInBatch = new Dictionary<int, BedrockItemStackResponseOutcome>();
            var acceptedUpdates = new List<KeyValuePair<BaselineKey, BedrockItemStackBaselineSlot>>();
            var freshKeys = new HashSet<BaselineKey>();
            for (int index = 0; index < responses.Count; index++)
            {
                BedrockItemStackResponse response = responses[index];
                ArgumentNullException.ThrowIfNull(response);
                if (firstOutcomeInBatch.TryGetValue(response.RequestId, out BedrockItemStackResponseOutcome first))
                {
                    outcomes[index] = first is BedrockItemStackResponseOutcome.Accepted or BedrockItemStackResponseOutcome.Rejected
                        ? BedrockItemStackResponseOutcome.DuplicateResponse
                        : first;
                    continue;
                }

                BedrockItemStackResponseOutcome outcome = Classify(response);
                firstOutcomeInBatch[response.RequestId] = outcome;
                outcomes[index] = outcome;
                if (outcome == BedrockItemStackResponseOutcome.Accepted)
                    CollectUpdates(response, acceptedUpdates, freshKeys);
            }

            int newKeys = 0;
            foreach (BaselineKey key in freshKeys)
            {
                if (!_baseline.ContainsKey(key))
                    newKeys++;
            }

            if (_baseline.Count + newKeys > MaxBaselineEntries)
                throw new InvalidOperationException("The item stack baseline holds too many entries for the response batch.");

            foreach (KeyValuePair<BaselineKey, BedrockItemStackBaselineSlot> update in acceptedUpdates)
                _baseline[update.Key] = update.Value;
            for (int index = 0; index < responses.Count; index++)
                Commit(responses[index].RequestId, outcomes[index]);

            return outcomes;
        }
    }

    /// <summary>Reads one authoritative baseline slot written by an accepted response, or null when no accepted response covered it.</summary>
    internal BedrockItemStackBaselineSlot? GetBaseline(byte containerId, uint? dynamicContainerId, byte slot)
    {
        lock (_gate)
            return _baseline.TryGetValue(new BaselineKey(containerId, dynamicContainerId, slot), out BedrockItemStackBaselineSlot baseline) ? baseline : null;
    }

    /// <summary>Drops pending registrations and the authoritative baseline while advancing the generation after a lifecycle boundary.</summary>
    internal void Invalidate()
    {
        lock (_gate)
        {
            _pending.Clear();
            _baseline.Clear();
            _generation++;
        }
    }

    /// <summary>Drops pending registrations while keeping the authoritative baseline and the current generation.</summary>
    internal void ClearPending()
    {
        lock (_gate)
            _pending.Clear();
    }

    private void RememberCompleted(int requestId)
    {
        if (_completed.ContainsKey(requestId))
        {
            _completed[requestId] = _generation;
            return;
        }

        while (_completed.Count >= MaxCompletedRequests && _completedOrder.TryDequeue(out int oldest))
            _completed.Remove(oldest);
        _completed[requestId] = _generation;
        _completedOrder.Enqueue(requestId);
    }

    private BedrockItemStackResponseOutcome Classify(BedrockItemStackResponse response)
    {
        if (!_pending.TryGetValue(response.RequestId, out int registeredGeneration))
        {
            if (_completed.TryGetValue(response.RequestId, out int completedGeneration) && completedGeneration == _generation)
                return BedrockItemStackResponseOutcome.DuplicateResponse;
            if (_completed.ContainsKey(response.RequestId))
                return BedrockItemStackResponseOutcome.StaleGeneration;
            return BedrockItemStackResponseOutcome.UnknownRequest;
        }

        if (registeredGeneration != _generation)
            return BedrockItemStackResponseOutcome.StaleGeneration;
        return response.IsOk ? BedrockItemStackResponseOutcome.Accepted : BedrockItemStackResponseOutcome.Rejected;
    }

    private void Commit(int requestId, BedrockItemStackResponseOutcome outcome)
    {
        switch (outcome)
        {
            case BedrockItemStackResponseOutcome.Accepted:
                _pending.Remove(requestId);
                RememberCompleted(requestId);
                _acceptedCount++;
                break;
            case BedrockItemStackResponseOutcome.Rejected:
                _pending.Remove(requestId);
                RememberCompleted(requestId);
                _rejectedCount++;
                break;
            case BedrockItemStackResponseOutcome.UnknownRequest:
                _unknownCount++;
                break;
            case BedrockItemStackResponseOutcome.DuplicateResponse:
                _duplicateCount++;
                break;
            case BedrockItemStackResponseOutcome.StaleGeneration:
                _pending.Remove(requestId);
                _staleCount++;
                break;
        }
    }

    private static void CollectUpdates(BedrockItemStackResponse response, List<KeyValuePair<BaselineKey, BedrockItemStackBaselineSlot>> acceptedUpdates, HashSet<BaselineKey> freshKeys)
    {
        foreach (BedrockItemStackResponseContainer container in response.Containers)
        {
            foreach (BedrockItemStackResponseSlot slot in container.Slots)
            {
                KeyValuePair<BaselineKey, BedrockItemStackBaselineSlot> update = MakeUpdate(container.SlotType, slot);
                acceptedUpdates.Add(update);
                freshKeys.Add(update.Key);
            }
        }
    }

    private static KeyValuePair<BaselineKey, BedrockItemStackBaselineSlot> MakeUpdate(BedrockFullContainerName slotType, BedrockItemStackResponseSlot slot) =>
        new(
            new BaselineKey(slotType.ContainerId, slotType.DynamicContainerId, slot.Slot),
            new BedrockItemStackBaselineSlot(slotType.ContainerId, slotType.DynamicContainerId, slot.Slot, slot.Count, slot.StackId, slot.CustomName, slot.FilteredCustomName, slot.DurabilityCorrection));

    private readonly record struct BaselineKey(byte ContainerId, uint? DynamicContainerId, byte Slot);
}
