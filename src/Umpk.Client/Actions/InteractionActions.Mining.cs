using Umpk.Client.State;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Actions;

public sealed partial class InteractionActions
{
    private MiningRequest? _mining;
    private int _destroyDelay;

    private sealed class MiningRequest(BlockPos position, Direction face, long playerLifetime, CancellationToken cancellation)
    {
        public BlockPos Position { get; } = position;
        public Direction Face { get; } = face;
        public long PlayerLifetime { get; } = playerLifetime;
        public CancellationToken Cancellation { get; } = cancellation;
        public TaskCompletionSource<int?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public World? World { get; set; }
        public ItemStack Item { get; set; } = ItemStack.Empty;
        public bool Started { get; set; }
        public float Progress { get; set; }
    }

    private sealed class MiningUnavailableException(string message) : InvalidOperationException(message);

    private MiningRequest QueueMining(BlockPos position, Direction face, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_mining is not null)
            throw new InvalidOperationException("A block attack is already active. Cancel it before starting another.");
        return _mining = new MiningRequest(position, face, _services.State.PlayerLifetime, ct);
    }

    internal async ValueTask TickMiningOnLoopAsync(CancellationToken ct)
    {
        MiningRequest? request = _mining;
        if (request is null)
            return;
        if (request.Cancellation.IsCancellationRequested)
        {
            await ReleaseMiningOnLoopAsync(request, ct).ConfigureAwait(false);
            return;
        }
        try
        {
            if (request.PlayerLifetime != _services.State.PlayerLifetime)
                throw new MiningUnavailableException("The mining player's lifetime has ended.");
            if (!_services.State.HasWorld || (_services.State.World.GetColumn(request.Position) is null))
                throw new MiningUnavailableException("Mining requires the target's loaded terrain.");
            World world = _services.State.World;
            if (request.World is { } previous && !ReferenceEquals(previous, world))
                throw new MiningUnavailableException("The mining world has ended.");
            request.World = world;
            if (_services.Self.GameMode is GameMode.Spectator or GameMode.Adventure)
                throw new MiningUnavailableException("This game mode requires an allowed block interaction before mining.");

            BlockState block = world.GetBlock(request.Position);
            if (!block.IsValid)
                throw new MiningUnavailableException("The target block state is unknown.");
            if (block.IsAir)
            {
                // A server update can remove the target while the held attack is running. It never authorizes an invented STOP or a local world mutation.
                if (!request.Started)
                    await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
                CompleteMining(request, null);
                return;
            }

            ItemStack item = HeldItem();
            float progress = ReadMiningProgress(block, item);
            if (!request.Started)
            {
                await StartMiningOnLoopAsync(request, item, progress, initialAttack: true, ct).ConfigureAwait(false);
                if (!ReferenceEquals(_mining, request))
                    return;
            }
            else if (!item.IsSameItemSameComponents(request.Item))
            {
                // Vanilla restarts a held attack when the selected stack's item or components change, even at the same position. The new START is followed by the continuation swing; it does not receive another click's initial swing.
                await SendAbortMiningAsync(request, request.Face, ct).ConfigureAwait(false);
                request.Started = false;
                request.Progress = 0;
                await StartMiningOnLoopAsync(request, item, progress, initialAttack: false, ct).ConfigureAwait(false);
                return;
            }

            if (_destroyDelay > 0)
            {
                _destroyDelay--;
                await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
                return;
            }

            if (_blockProfile.CanBreakInstantly(_services.Self))
            {
                int? sequence = NextSequence();
                await _sink.SendAsync(new ServerboundPlayerActionPacket(0, request.Position, (byte)request.Face, sequence), ct).ConfigureAwait(false);
                _destroyDelay = 5;
                await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
                CompleteMining(request, sequence);
                return;
            }

            // Float accumulation matters: bare-hand stone reaches one after 151 updates, not the rounded mathematical estimate of 150.
            request.Progress += progress;
            int? finished = null;
            if (request.Progress >= 1.0F)
            {
                finished = NextSequence();
                await _sink.SendAsync(new ServerboundPlayerActionPacket(_blockProfile.StopDestroyAction, request.Position, (byte)request.Face, finished), ct).ConfigureAwait(false);
                _destroyDelay = 5;
            }
            await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
            if (request.Progress >= 1.0F)
                CompleteMining(request, finished);
        }
        catch (MiningUnavailableException error)
        {
            if (request.Started && request.PlayerLifetime == _services.State.PlayerLifetime && ReferenceEquals(request.World, _services.State.HasWorld ? _services.State.World : null))
                await SendAbortMiningAsync(request, Direction.Down, ct).ConfigureAwait(false);
            FailMining(request, error);
        }
        catch (Exception error)
        {
            // Transport and unexpected controller failures retain the tick pipeline's failure behavior, while also releasing the caller waiting for this operation.
            FailMining(request, error);
            throw;
        }
    }

    private async ValueTask StartMiningOnLoopAsync(MiningRequest request, ItemStack item, float progress, bool initialAttack, CancellationToken ct)
    {
        if (initialAttack && _blockProfile.InitialSwingBeforeStart)
            await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
        int? sequence = NextSequence();
        await _sink.SendAsync(new ServerboundPlayerActionPacket(0, request.Position, (byte)request.Face, sequence), ct).ConfigureAwait(false);
        request.Started = true;
        request.Item = item;
        if (!initialAttack || !_blockProfile.InitialSwingBeforeStart)
            await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
        if (_blockProfile.CanBreakInstantly(_services.Self) || progress >= 1.0F)
        {
            if (_blockProfile.CanBreakInstantly(_services.Self))
                _destroyDelay = 5;
            CompleteMining(request, sequence);
        }
    }

    private void CompleteMining(MiningRequest request, int? sequence)
    {
        if (ReferenceEquals(_mining, request))
            _mining = null;
        request.Completion.TrySetResult(sequence);
    }

    private void FailMining(MiningRequest request, Exception error)
    {
        if (ReferenceEquals(_mining, request))
            _mining = null;
        request.Completion.TrySetException(error);
    }

    private ValueTask SendAbortMiningAsync(MiningRequest request, Direction face, CancellationToken ct)
        => _sink.SendAsync(new ServerboundPlayerActionPacket(_blockProfile.AbortDestroyAction, request.Position, (byte)face, HasSequences() ? 0 : null), ct);

    private async ValueTask ReleaseMiningOnLoopAsync(MiningRequest request, CancellationToken session)
    {
        if (!ReferenceEquals(_mining, request))
            return;
        _mining = null;
        request.Completion.TrySetCanceled(request.Cancellation.IsCancellationRequested ? request.Cancellation : new CancellationToken(true));
        if (request.Started && !session.IsCancellationRequested && request.PlayerLifetime == _services.State.PlayerLifetime && _services.State.HasWorld && ReferenceEquals(request.World, _services.State.World))
            await SendAbortMiningAsync(request, Direction.Down, session).ConfigureAwait(false);
    }

    private ValueTask CancelMiningOnLoopAsync(BlockPos position, CancellationToken ct)
        => _mining is { } request && request.Position == position ? ReleaseMiningOnLoopAsync(request, ct) : ValueTask.CompletedTask;

    private float ReadMiningProgress(BlockState block, ItemStack item)
    {
        int protocol = _services.Version.Version.Protocol;
        IBlockMiningSource source = JavaGameData.BlockMiningData(protocol);
        Identifier itemId = item.IsEmpty ? Identifier.Minecraft("air") : item.Item.Id;
        if (!source.TryGetBlock(block.Block.Id, out BlockMiningInfo hardness) || !source.TryGetTool(itemId, block.Block.Id, out ToolMiningInfo tool))
            throw new MiningUnavailableException("No vanilla mining measurement is available for this block and item.");
        if (_blockProfile.CanBreakInstantly(_services.Self))
            return 1.0F;
        if (hardness.Hardness < 0)
            throw new MiningUnavailableException("The target has unbreakable vanilla hardness.");
        if (_blockProfile.ItemToolComponents)
        {
            // Component-era stacks carry measured defaults plus their server patch. No effective Tool component means ordinary hand speed, including when the patch explicitly removed a pickaxe's default Tool.
            tool = new ToolMiningInfo(1.0F, false);
            if (item.Components.TryGet(DataComponents.Tool, out ToolComponent? overridden) && !VanillaToolRules.TryResolve(overridden, block.Block.NetworkId, _services.State.RegistryTags, out tool))
                throw new MiningUnavailableException("The tool's mining rules require unavailable tag membership or an unsupported mining speed.");
        }

        RegistryAccess registries = _services.State.Registries ?? JavaGameData.Registries(protocol);
        bool efficiencyAttribute = HasMiningAttribute(registries, "mining_efficiency");
        float efficiency = efficiencyAttribute ? (float)_services.Self.Attributes.Value(Identifier.Minecraft("mining_efficiency"), 0) : 0;
        if (!efficiencyAttribute && item.TryGetEnchantmentLevel(Identifier.Minecraft("efficiency"), JavaGameData.LegacyItemBridgeEra, JavaGameData.LegacyItemBridgeSource, out int level) && level > 0)
            efficiency = level * (float)level + 1.0F;
        float submerged = (float)_services.Self.Attributes.Value(Identifier.Minecraft("submerged_mining_speed"), 0.2);
        if (!HasMiningAttribute(registries, "submerged_mining_speed") && _services.State.Features.Inventory)
        {
            ItemStack helmet = _services.State.Inventory.PlayerSlots[5];
            if (helmet.TryGetEnchantmentLevel(Identifier.Minecraft("aqua_affinity"), JavaGameData.LegacyItemBridgeEra, JavaGameData.LegacyItemBridgeSource, out int aqua) && aqua > 0)
                submerged = 1.0F;
        }
        var modifiers = new MiningModifiers(
            efficiency,
            Math.Max(MiningEffectAmplifier(registries, "haste"), MiningEffectAmplifier(registries, "conduit_power")),
            MiningEffectAmplifier(registries, "mining_fatigue"),
            (float)_services.Self.Attributes.Value(Identifier.Minecraft("block_break_speed"), 1),
            submerged,
            _services.ReadPhysicsState()?.IsUnderWater ?? false,
            _services.Self.OnGround);
        return VanillaMiningProgress.Compute(hardness, tool, modifiers, _blockProfile.ExponentialMiningFatigue);
    }

    private static bool HasMiningAttribute(RegistryAccess registries, string name)
        => registries.Attributes.TryGetNetworkId(Identifier.Minecraft(name), out _) || registries.Attributes.TryGetNetworkId(Identifier.Minecraft("generic." + name), out _);

    private int MiningEffectAmplifier(RegistryAccess registries, string name)
    {
        if (!registries.MobEffects.TryGetNetworkId(Identifier.Minecraft(name), out int id) || !_services.Self.ActiveEffects.TryGetValue(id, out ActiveEffect? effect))
            return -1;
        return effect.RemainingTicksAt(_services.State.SessionTick) == 0 ? -1 : effect.Amplifier;
    }
}
