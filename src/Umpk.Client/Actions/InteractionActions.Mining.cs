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
    private MiningTarget? _destroyTarget;
    private readonly VanillaAttackCharge _attackCharge = new();
    private long? _interactionLifetime;
    private int _destroyDelay;

    private sealed class MiningRequest(BlockPos position, Direction face, double? viewDistance, long playerLifetime, CancellationToken cancellation)
    {
        public BlockPos Position { get; } = position;
        public Direction Face { get; set; } = face;
        public double? ViewDistance { get; } = viewDistance;
        public long PlayerLifetime { get; } = playerLifetime;
        public CancellationToken Cancellation { get; } = cancellation;
        public TaskCompletionSource<int?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public World? World { get; set; }
        public bool InitialAttackPending { get; set; } = true;
    }

    // The game mode retains its target/item after stopDestroyBlock. Its active flag is separate from the held request and from this cached identity.
    private sealed class MiningTarget(MiningRequest request, ItemStack item)
    {
        public BlockPos Position { get; } = request.Position;
        public long PlayerLifetime { get; } = request.PlayerLifetime;
        public WeakReference<World> World { get; } = new(request.World!);
        public ItemStack Item { get; } = item;
        public Direction? DestroyDirection { get; set; } = request.Face;
        public bool Active { get; set; } = true;
        public float Progress { get; set; }
    }

    private sealed class MiningUnavailableException(string message) : InvalidOperationException(message);

    private MiningRequest QueueMining(BlockPos position, Direction face, double? viewDistance, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_mining is not null)
            throw new InvalidOperationException("A block attack is already active. Cancel it before starting another.");
        if (viewDistance is double distance)
        {
            if (_services.ReadBlockHit(distance) is not { Hit: true } hit || hit.BlockPos != position)
                throw new OperationCanceledException("The requested block is no longer in view.", new CancellationToken(true));
            face = hit.Face;
        }
        return _mining = new MiningRequest(position, face, viewDistance, _services.State.PlayerLifetime, ct);
    }

    internal void TickAttackChargeOnLoop(bool playerTicking)
    {
        _attackCharge.Synchronize(_services.State.PlayerLifetime);
        if (_blockProfile.ChargedPiercingAttack && playerTicking)
            _attackCharge.Advance(_services.State.PlayerLifetime, HeldItem());
    }

    internal async ValueTask TickMiningOnLoopAsync(CancellationToken ct)
    {
        SynchronizeMiningOwner();
        MiningRequest? request = _mining;
        if (request is null)
        {
            await StopReleasedMiningOnLoopAsync(ct).ConfigureAwait(false);
            return;
        }
        if (request.Cancellation.IsCancellationRequested)
        {
            await ReleaseMiningOnLoopAsync(request, ct).ConfigureAwait(false);
            return;
        }
        try
        {
            if (request.PlayerLifetime != _services.State.PlayerLifetime)
                throw new MiningUnavailableException("The mining player's lifetime has ended.");
            if (!_services.State.HasWorld || _services.State.World.GetColumn(request.Position) is null)
                throw new MiningUnavailableException("Mining requires the target's loaded terrain.");
            World world = _services.State.World;
            if (request.World is { } previous && !ReferenceEquals(previous, world))
                throw new MiningUnavailableException("The mining world has ended.");
            request.World = world;
            if (_services.Self.GameMode is GameMode.Spectator or GameMode.Adventure)
                throw new MiningUnavailableException("This game mode requires an allowed block interaction before mining.");

            if (request.ViewDistance is double distance)
            {
                if (_services.ReadBlockHit(distance) is not { Hit: true } hit || hit.BlockPos != request.Position)
                {
                    await ReleaseMiningOnLoopAsync(request, ct).ConfigureAwait(false);
                    return;
                }
                request.Face = hit.Face;
            }

            ItemStack item = HeldItem();
            bool piercing = IsPiercing(item);
            if (request.InitialAttackPending)
            {
                request.InitialAttackPending = false;
                if (!InitialAttackIsBlocked(item))
                {
                    if (piercing)
                    {
                        await _sink.SendAsync(new ServerboundPlayerActionPacket(_blockProfile.StabAction, default, (byte)Direction.Down, 0), ct).ConfigureAwait(false);
                        _attackCharge.Reset();
                        if (!_blockProfile.UseSwingIsLocal)
                            await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
                        return;
                    }

                    BlockState initialBlock = world.GetBlock(request.Position);
                    if (!initialBlock.IsValid)
                        throw new MiningUnavailableException("The target block state is unknown.");
                    if (initialBlock.IsAir)
                    {
                        _attackCharge.Reset();
                        await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
                        CompleteMining(request, null);
                        return;
                    }

                    if (_blockProfile.InitialSwingBeforeStart)
                        await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
                    await StartMiningTargetOnLoopAsync(request, item, ReadMiningProgress(initialBlock, item), ct).ConfigureAwait(false);
                    if (!_blockProfile.InitialSwingBeforeStart)
                        await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
                    if (!ReferenceEquals(_mining, request))
                    {
                        await StopReleasedMiningOnLoopAsync(ct).ConfigureAwait(false);
                        return;
                    }
                }
            }

            // The piercing guard covers the whole continuation, including ordinary release. A later charge-ready tick is not another click.
            if (piercing)
                return;

            BlockState block = world.GetBlock(request.Position);
            if (!block.IsValid)
                throw new MiningUnavailableException("The target block state is unknown.");
            if (block.IsAir)
            {
                // Complete the one-block request without inventing a STOP or mutating server-owned terrain.
                CompleteMining(request, null);
                return;
            }

            if (_destroyDelay > 0)
            {
                _destroyDelay--;
                await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
                return;
            }

            float progress = ReadMiningProgress(block, item);
            if (_blockProfile.CanBreakInstantly(_services.Self) || !SameDestroyTarget(request.Position, item))
            {
                await StartMiningTargetOnLoopAsync(request, item, progress, ct).ConfigureAwait(false);
                await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
                return;
            }

            MiningTarget target = _destroyTarget!;
            // Float accumulation matters: bare-hand stone reaches one after 151 updates, not the rounded mathematical estimate of 150.
            target.Progress += progress;
            int? finished = null;
            bool complete = target.Progress >= 1.0F;
            if (complete)
            {
                target.Active = false;
                finished = NextSequence();
                await _sink.SendAsync(new ServerboundPlayerActionPacket(_blockProfile.StopDestroyAction, request.Position, (byte)request.Face, finished), ct).ConfigureAwait(false);
                target.Progress = 0;
                target.DestroyDirection = null;
                _destroyDelay = 5;
            }
            else if (_blockProfile.HasDestroyDirectionAction && target.DestroyDirection != request.Face)
            {
                target.DestroyDirection = request.Face;
                await _sink.SendAsync(new ServerboundPlayerActionPacket(1, request.Position, (byte)request.Face, 0), ct).ConfigureAwait(false);
            }
            await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
            if (complete)
                CompleteMining(request, finished);
        }
        catch (MiningUnavailableException error)
        {
            if (request.PlayerLifetime == _services.State.PlayerLifetime && ReferenceEquals(request.World, _services.State.WorldOrNull))
                await StopReleasedMiningOnLoopAsync(ct).ConfigureAwait(false);
            FailMining(request, error);
        }
        catch (Exception error)
        {
            // Transport and unexpected controller failures retain the tick pipeline's failure behavior and release the waiting caller.
            FailMining(request, error);
            throw;
        }
    }

    private bool IsPiercing(ItemStack item)
        => _blockProfile.ChargedPiercingAttack && item.Components.Has(DataComponents.PiercingWeapon);

    private bool InitialAttackIsBlocked(ItemStack item)
    {
        if (!_blockProfile.ChargedPiercingAttack || item.Components.Get(DataComponents.MinimumAttackCharge) is not { } charge || !(charge.Value > 0))
            return false;
        Identifier id = Identifier.Minecraft("attack_speed");
        RegistryAccess registries = _services.State.Registries ?? JavaGameData.Registries(_services.Version.Version.Protocol);
        if (!registries.Attributes.TryGetValue(id, out AttributeDefinition? attribute))
            throw new MiningUnavailableException("The attack-speed attribute is unavailable.");
        double speed = _services.Self.Attributes.Value(id, attribute.DefaultValue);
        return VanillaAttackCharge.IsBlocked(_attackCharge.StrengthTicks, speed, charge.Value);
    }

    private bool SameDestroyTarget(BlockPos position, ItemStack item)
        => _destroyTarget is { } target && target.Position == position && VanillaMiningItemIdentity.Matches(item, target.Item, _services.Version.Version.Protocol);

    private void SynchronizeMiningOwner()
    {
        if (_interactionLifetime != _services.State.InteractionLifetime)
        {
            _interactionLifetime = _services.State.InteractionLifetime;
            _destroyDelay = 0;
            _destroyTarget = null;
        }
        _attackCharge.Synchronize(_services.State.PlayerLifetime);
        if (_destroyTarget is { } target
            && (target.PlayerLifetime != _services.State.PlayerLifetime || !target.World.TryGetTarget(out World? world) || !ReferenceEquals(world, _services.State.WorldOrNull)))
            _destroyTarget = null;
    }

    private async ValueTask StartMiningTargetOnLoopAsync(MiningRequest request, ItemStack item, float progress, CancellationToken ct)
    {
        bool instantBuild = _blockProfile.CanBreakInstantly(_services.Self);
        if (!instantBuild && _destroyTarget is { Active: true } previous)
        {
            if (SameDestroyTarget(request.Position, item))
                return;
            await SendAbortMiningAsync(previous.Position, request.Face, ct).ConfigureAwait(false);
        }

        int? sequence = NextSequence();
        await _sink.SendAsync(new ServerboundPlayerActionPacket(0, request.Position, (byte)request.Face, sequence), ct).ConfigureAwait(false);
        if (instantBuild || progress >= 1.0F)
        {
            if (instantBuild)
                _destroyDelay = 5;
            CompleteMining(request, sequence);
        }
        else
            _destroyTarget = new MiningTarget(request, item);
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

    private ValueTask SendAbortMiningAsync(BlockPos position, Direction face, CancellationToken ct)
        => _sink.SendAsync(new ServerboundPlayerActionPacket(_blockProfile.AbortDestroyAction, position, (byte)face, HasSequences() ? 0 : null), ct);

    internal async ValueTask StopDestroyBlockOnLoopAsync(CancellationToken ct)
    {
        SynchronizeMiningOwner();
        if (_destroyTarget is not { Active: true } target)
            return;
        await SendAbortMiningAsync(target.Position, Direction.Down, ct).ConfigureAwait(false);
        target.Active = false;
        target.Progress = 0;
        _attackCharge.Reset();
    }

    private ValueTask StopReleasedMiningOnLoopAsync(CancellationToken ct)
        => IsPiercing(HeldItem()) ? ValueTask.CompletedTask : StopDestroyBlockOnLoopAsync(ct);

    private async ValueTask ReleaseMiningOnLoopAsync(MiningRequest request, CancellationToken session)
    {
        if (!ReferenceEquals(_mining, request))
            return;
        _mining = null;
        request.Completion.TrySetCanceled(request.Cancellation.IsCancellationRequested ? request.Cancellation : new CancellationToken(true));
        if (!session.IsCancellationRequested && request.PlayerLifetime == _services.State.PlayerLifetime && ReferenceEquals(request.World, _services.State.WorldOrNull))
            await StopReleasedMiningOnLoopAsync(session).ConfigureAwait(false);
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
            return 0.0F;
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
        bool legacyUnderwaterPenalty = !HasMiningAttribute(registries, "submerged_mining_speed");
        if (legacyUnderwaterPenalty && _services.State.Features.Inventory)
        {
            // Native Aqua Affinity selects all armor slots, including server-supplied enchantments outside the ordinary helmet slot.
            for (int slot = 5; slot <= 8; slot++)
            {
                ItemStack armor = _services.State.Inventory.PlayerSlots[slot];
                if (armor.TryGetEnchantmentLevel(Identifier.Minecraft("aqua_affinity"), JavaGameData.LegacyItemBridgeEra, JavaGameData.LegacyItemBridgeSource, out int aqua) && aqua > 0)
                {
                    submerged = 1.0F;
                    legacyUnderwaterPenalty = false;
                    break;
                }
            }
        }
        var modifiers = new MiningModifiers(
            efficiency,
            Math.Max(MiningEffectAmplifier(registries, "haste"), MiningEffectAmplifier(registries, "conduit_power")),
            MiningEffectAmplifier(registries, "mining_fatigue"),
            (float)_services.Self.Attributes.Value(Identifier.Minecraft("block_break_speed"), 1),
            submerged,
            _services.ReadPhysicsState()?.IsUnderWater ?? false,
            _services.Self.OnGround)
        {
            LegacyUnderwaterPenalty = legacyUnderwaterPenalty,
        };
        return VanillaMiningProgress.Compute(hardness, tool, modifiers, _blockProfile.ExponentialMiningFatigue);
    }

    private static bool HasMiningAttribute(RegistryAccess registries, string name)
        => registries.Attributes.TryGetNetworkId(Identifier.Minecraft(name), out _)
            || registries.Attributes.TryGetNetworkId(Identifier.Minecraft("player." + name), out _)
            || registries.Attributes.TryGetNetworkId(Identifier.Minecraft("generic." + name), out _);

    private int MiningEffectAmplifier(RegistryAccess registries, string name)
    {
        if (!registries.MobEffects.TryGetNetworkId(Identifier.Minecraft(name), out int id) || !_services.Self.ActiveEffects.TryGetValue(id, out ActiveEffect? effect))
            return -1;
        return effect.RemainingTicksAt(_services.State.SessionTick) == 0 ? -1 : effect.Amplifier;
    }
}
