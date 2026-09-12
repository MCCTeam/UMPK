using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Snapshots;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Actions;

/// <summary>The hand used for an interaction.</summary>
public enum Hand
{
    /// <summary>The main hand.</summary>
    Main = 0,

    /// <summary>The off hand.</summary>
    Off = 1,
}

/// <summary>Block/entity interaction. Dig uses the player-action start/finish sequence; place/use carry the 1.19+ action sequence where the version exposes it. On versions with the block-change-ack protocol, dig/place complete when the server acknowledges the action's sequence, bounded by a timeout that falls back to write-completion; on pre-1.19 versions completion is the write.</summary>
public sealed class InteractionActions
{
    private readonly IPacketSink _sink;
    private readonly ClientSessionServices _services;
    private readonly SequenceTracker _sequences;

    internal InteractionActions(IPacketSink sink, ClientSessionServices services, SequenceTracker sequences)
    {
        _sink = sink;
        _services = services;
        _sequences = sequences;
    }

    /// <summary>Digs a block: sends start-digging then finish-digging, plus a swing.</summary>
    public async Task DigBlockAsync(BlockPos position, Direction face = Direction.Up, CancellationToken ct = default)
    {
        int? startSeq = NextSequence();
        await _sink.SendAsync(new ServerboundPlayerActionPacket(0, position, (byte)face, startSeq), ct).ConfigureAwait(false);
        await SwingAsync(Hand.Main, ct).ConfigureAwait(false);
        int? finishSeq = NextSequence();
        await _sink.SendAsync(new ServerboundPlayerActionPacket(2, position, (byte)face, finishSeq), ct).ConfigureAwait(false);
        if (finishSeq is int fs && HasSequences())
            await AwaitAckAsync(fs, ct).ConfigureAwait(false);

    }

    /// <summary>Cancels an in-progress dig at a position.</summary>
    public Task CancelDigAsync(BlockPos position, Direction face = Direction.Up, CancellationToken ct = default)
        => _sink.SendAsync(new ServerboundPlayerActionPacket(1, position, (byte)face, NextSequence()), ct).AsTask();

    /// <summary>How long <see cref="DigBlockVerifiedAsync"/> waits for the server to say what happened to the block before it gives up and reports <see cref="DigOutcome.Unconfirmed"/>.</summary>
    public static readonly TimeSpan DefaultDigConfirmationWindow = TimeSpan.FromSeconds(1);

    /// <summary>The re-read cadence inside the confirmation window.</summary>
    private static readonly TimeSpan DigConfirmationPoll = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Digs a block through the same start/finish action sequence as <see cref="DigBlockAsync"/>, and then reports what the server is known to have done about it, instead of reporting the completed send as a broken block.
    ///
    /// <para>Sending the start/finish pair is not breaking a block. A survival player who has not accumulated enough mining progress, a protected region, a spectator, and a block the server simply refuses all produce exactly the same successful write, and vanilla answers all of them by sending the block back unchanged. The only honest report is the one taken from the world afterwards, so this waits (briefly) for the position to settle and then reads it: air means the block went, an update that left the block standing means the server refused, and neither means the client does not know.</para>
    ///
    /// <para>The wait polls the tracked world state as well as listening for <see cref="BlockChanged"/>, because the event fires only for the single-block <c>block_update</c> packet (a batched <c>section_blocks_update</c> updates the world silently). Reading the world is therefore the load-bearing check, and the event only sharpens <see cref="DigOutcome.NotBroken"/>.</para>
    /// </summary>
    /// <param name="position">The block to dig.</param>
    /// <param name="face">The face the dig is aimed at.</param>
    /// <param name="confirmationWindow">How long to wait for a settled answer before reporting <see cref="DigOutcome.Unconfirmed"/>; defaults to <see cref="DefaultDigConfirmationWindow"/>.</param>
    /// <param name="ct">Cancels the send and the wait.</param>
    /// <exception cref="InvalidOperationException"><see cref="ClientSessionServices.Events"/> is null on this session. A poll-only read cannot tell an observed refusal (the server re-asserting the block) from silence, so this refuses rather than degrading to a guess.</exception>
    public async Task<DigOutcome> DigBlockVerifiedAsync(
        BlockPos position, Direction face = Direction.Up, TimeSpan? confirmationWindow = null, CancellationToken ct = default)
    {
        EventBus events = _services.Events
            ?? throw new InvalidOperationException(
                $"{nameof(DigBlockVerifiedAsync)} requires an event bus (ClientSessionServices.Events), and none is wired on this session.");

        TimeSpan window = confirmationWindow ?? DefaultDigConfirmationWindow;

        // Armed BEFORE the send: on a fast local server the block update can be applied while the dig action is still awaiting its own acknowledgement, and a subscription taken afterwards would miss it.
        var corrected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = events.Subscribe<BlockChanged>(change =>
        {
            if (change.Position == position)
                corrected.TrySetResult(true);

        });

        await DigBlockAsync(position, face, ct).ConfigureAwait(false);

        long deadline = Environment.TickCount64 + (long)window.TotalMilliseconds;
        while (true)
        {
            Umpk.Game.World.World world = _services.State.World;
            bool loaded = world.GetColumn(position) is not null;
            if (loaded && world.GetBlock(position).IsAir)
                return DigOutcome.Broken;

            if (corrected.Task.IsCompleted)
            {
                // The server sent this exact position back and the block is still standing: a refusal we actually observed, not a guess from silence.
                return loaded ? DigOutcome.NotBroken : DigOutcome.Unconfirmed;
            }

            long remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
                return DigOutcome.Unconfirmed;

            TimeSpan slice = TimeSpan.FromMilliseconds(Math.Min(remaining, (long)DigConfirmationPoll.TotalMilliseconds));
            await Task.WhenAny(corrected.Task, Task.Delay(slice, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>The interaction reach a plan and this action agree on: <b>4.5 blocks, eye to the nearest point of the target block's unit cube.</b></summary>
    /// <remarks>
    /// <para>One number covers every supported protocol. Modern servers allow 4.5 blocks from the eye to the nearest point of the target bounds. Earlier servers allow a greater eye-to-centre or feet-to-centre distance, so the modern limit is safe for every supported version.</para>
    /// <para>The check is here and not only in the planner because it is cheap and because failing it silently is expensive: an out-of-range <c>use_item_on</c> is a packet the server drops, and the verify loop then spends its whole window waiting for an answer nobody is going to send.</para>
    /// <para>It is the planner's own constant rather than a second copy of the number, so a plan can never promise a press this action then refuses.</para>
    /// </remarks>
    public const double BlockInteractionRange = Umpk.Pathfinding.Moves.DoorActivation.InteractReach;

    /// <summary>Uses a block and then reports what the server is known to have done about it, by reading a witness block's own property back out of the tracked world.</summary>
    /// <remarks>
    /// <para><b>Why a witness, and why a read.</b> Sending <c>use_item_on</c> is not opening a door. A protected region, a spectator, adventure mode, a plugin that vetoes the interaction and a server that simply refuses all produce the same successful write, and vanilla answers every one of them by sending the block back unchanged - which is exactly the reasoning <see cref="DigBlockVerifiedAsync"/> exists for, on a different verb.</para>
    /// <para>The witness is a separate position because the block that is USED and the block that ANSWERS are not always the same one. Pressing a button to open an iron door is one <c>use_item_on</c> against the button and one reading of the door: A powered door keeps its <c>POWERED</c> and <c>OPEN</c> properties in step with the signal, so the door itself is the authoritative answer to "did the press do anything". For a hand-opened door the two are the same position and the call reads as it looks.</para>
    /// <para><b>No rotation.</b> The server never validates look direction: the only geometric check on the hit vector is <c>abs(hit - centre) &lt; 1.0000001</c> per axis, and yaw and pitch are not consulted. So this sends the block-centre cursor, touches nothing about where the player is looking, and never contends with the movement templates for the engine's rotation.</para>
    /// <para>The wait polls the tracked world as well as listening for <see cref="BlockChanged"/>, for the reason <see cref="DigBlockVerifiedAsync"/> records: the event fires only for the single-block <c>block_update</c> packet, and a batched <c>section_blocks_update</c> updates the world silently. The world read is load-bearing; the event only sharpens <see cref="UseBlockOutcome.NotUsed"/>.</para>
    /// </remarks>
    /// <param name="target">The block to send the <c>use_item_on</c> against.</param>
    /// <param name="witness">The block whose property is read back to decide the outcome.</param>
    /// <param name="property">The witness's property name, e.g. <c>open</c>.</param>
    /// <param name="expectedValue">The value that means the interaction worked, e.g. <c>true</c>.</param>
    /// <param name="face">The face the use is aimed at. Nothing validates it; it is here so a packet capture reads sensibly.</param>
    /// <param name="confirmationWindow">How long to wait for a settled answer before reporting <see cref="UseBlockOutcome.Unconfirmed"/>; defaults to <see cref="DefaultDigConfirmationWindow"/>, the same one second.</param>
    /// <param name="ct">Cancels the send and the wait.</param>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> or <paramref name="expectedValue"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="ClientSessionServices.Events"/> is null on this session. A poll-only read cannot tell an observed refusal (the server re-asserting the block) from silence, so this refuses rather than degrading to a guess - the same contract <see cref="DigBlockVerifiedAsync"/> carries.</exception>
    public async Task<UseBlockOutcome> UseBlockVerifiedAsync(
        BlockPos target,
        BlockPos witness,
        string property,
        string expectedValue,
        Direction face = Direction.Up,
        TimeSpan? confirmationWindow = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(expectedValue);

        EventBus events = _services.Events
            ?? throw new InvalidOperationException(
                $"{nameof(UseBlockVerifiedAsync)} requires an event bus (ClientSessionServices.Events), and none is wired on this session.");

        // Both refusals happen BEFORE the send, because both describe a packet the server would do nothing with, and a send nobody acts on costs a whole confirmation window.
        if (!IsWithinReach(target))
            return UseBlockOutcome.OutOfReach;

        if (_services.Self.Sneaking && !HeldItem().IsEmpty)
        {
            // When secondary use is active and a hand holds an item, the held item is used on the block instead of invoking the block's own action. Against a door this means trying to place the held item. Sneaking with empty hands still opens the door, so the refusal is on the pair.
            return UseBlockOutcome.NotUsed;
        }

        TimeSpan window = confirmationWindow ?? DefaultDigConfirmationWindow;

        // Armed BEFORE the send, for DigBlockVerifiedAsync's reason: on a fast local server the block update can be applied while the use is still awaiting its own acknowledgement.
        var corrected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = events.Subscribe<BlockChanged>(change =>
        {
            if (change.Position == witness)
                corrected.TrySetResult(true);

        });

        await PlaceBlockAsync(target, face, BlockCentreCursor, Hand.Main, insideBlock: false, ct)
            .ConfigureAwait(false);

        long deadline = Environment.TickCount64 + (long)window.TotalMilliseconds;
        while (true)
        {
            Umpk.Game.World.World world = _services.State.World;
            bool loaded = world.GetColumn(witness) is not null;
            if (loaded
                && world.GetBlock(witness).TryGetProperty(property, out string value)
                && value == expectedValue)
                return UseBlockOutcome.Used;

            if (corrected.Task.IsCompleted)
            {
                // The server sent this exact position back and the property still reads the old value: a refusal actually observed, rather than a guess taken from silence.
                return loaded ? UseBlockOutcome.NotUsed : UseBlockOutcome.Unconfirmed;
            }

            long remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
                return UseBlockOutcome.Unconfirmed;

            TimeSpan slice = TimeSpan.FromMilliseconds(Math.Min(remaining, (long)DigConfirmationPoll.TotalMilliseconds));
            await Task.WhenAny(corrected.Task, Task.Delay(slice, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Waits for a block's own property to read a given value, and sends NOTHING.</summary>
    /// <remarks>
    /// <para>This is <see cref="UseBlockVerifiedAsync"/> with the send taken out, for the one barrier nothing is sent at: a door powered by a pressure plate. The plate responds to an entity standing on it rather than a use action, so the body standing on it is the interaction and the only thing left to do is find out when the server agrees.</para>
    /// <para><b>Why not simply send one anyway and let it be a no-op.</b> It would not be a no-op. When the block does not consume a use action, the held item receives it instead, so a <c>use_item_on</c> against a plate can attempt to place whatever the bot is holding. And <see cref="UseBlockVerifiedAsync"/> refuses outright while sneaking with a held item, because the server applies the secondary-use gate, which would score an already-open door as a refusal and spend an interaction budget on it.</para>
    /// <para>The two refusals <see cref="UseBlockVerifiedAsync"/> makes before its send are absent here for the same reason the send is: reach and the sneak-with-item gate are both properties of a packet, and there is no packet. A body that cannot reach the plate is a body that is not standing on it, which is a planning error rather than a send this can decline.</para>
    /// <para>The wait is the same loop and the same event/poll pair, for the reason <see cref="DigBlockVerifiedAsync"/> records: <see cref="BlockChanged"/> fires only for the single-block <c>block_update</c> packet and a batched <c>section_blocks_update</c> moves the world silently, so the world read is the load-bearing half.</para>
    /// </remarks>
    /// <param name="witness">The block whose property is read to decide the outcome.</param>
    /// <param name="property">The witness's property name, e.g. <c>open</c>.</param>
    /// <param name="expectedValue">The value that means the world did what was wanted.</param>
    /// <param name="confirmationWindow">How long to wait before reporting <see cref="UseBlockOutcome.Unconfirmed"/>; defaults to <see cref="DefaultDigConfirmationWindow"/>.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns><see cref="UseBlockOutcome.Used"/> once the witness reads the expected value, <see cref="UseBlockOutcome.Unconfirmed"/> if the window expires first. Never <see cref="UseBlockOutcome.NotUsed"/>: with no send there is no refusal to observe, and a server re-asserting the block says nothing about a signal that arrives from a body's own position.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> or <paramref name="expectedValue"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="ClientSessionServices.Events"/> is null on this session.</exception>
    public async Task<UseBlockOutcome> ObserveBlockPropertyAsync(
        BlockPos witness,
        string property,
        string expectedValue,
        TimeSpan? confirmationWindow = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(expectedValue);

        EventBus events = _services.Events
            ?? throw new InvalidOperationException(
                $"{nameof(ObserveBlockPropertyAsync)} requires an event bus (ClientSessionServices.Events), and none is wired on this session.");

        TimeSpan window = confirmationWindow ?? DefaultDigConfirmationWindow;
        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = events.Subscribe<BlockChanged>(change =>
        {
            if (change.Position == witness)
                changed.TrySetResult(true);

        });

        long deadline = Environment.TickCount64 + (long)window.TotalMilliseconds;
        while (true)
        {
            Umpk.Game.World.World world = _services.State.World;
            if (world.GetColumn(witness) is not null
                && world.GetBlock(witness).TryGetProperty(property, out string value)
                && value == expectedValue)
                return UseBlockOutcome.Used;

            long remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
                return UseBlockOutcome.Unconfirmed;

            TimeSpan slice = TimeSpan.FromMilliseconds(Math.Min(remaining, (long)DigConfirmationPoll.TotalMilliseconds));
            await Task.WhenAny(changed.Task, Task.Delay(slice, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>The block-centre hit vector. Any offset satisfies the server's only geometric check (<c>abs(hit - centre) &lt; 1.0000001</c> per axis), and the centre satisfies it on every face.</summary>
    private static readonly Vec3d BlockCentreCursor = new(0.5, 0.5, 0.5);

    /// <summary>Whether the player's eye is within <see cref="BlockInteractionRange"/> of the nearest point of the target's unit cube.</summary>
    private bool IsWithinReach(BlockPos target)
    {
        Vec3d feet = _services.Self.Position;
        double eyeY = feet.Y + Umpk.Physics.PhysicsConstants.PlayerStandingEyeHeight;

        double dx = Nearest(feet.X, target.X);
        double dy = Nearest(eyeY, target.Y);
        double dz = Nearest(feet.Z, target.Z);
        return (dx * dx) + (dy * dy) + (dz * dz) <= BlockInteractionRange * BlockInteractionRange;

        // The gap between a coordinate and the [min, min+1] span of the cube on that axis; zero inside.
        static double Nearest(double value, int min)
            => value < min ? min - value : value > min + 1 ? value - (min + 1) : 0.0;
    }

    /// <summary>Places / uses an item against a block face at a hit position.</summary>
    /// <remarks>1.8 has no <c>use_item_on</c>: the whole interaction travels on <c>block_place</c>, a different record with a different field set. This routes on the version rather than on a codec. Nothing routed before, so on protocol 47 the send resolved no outbound entry and threw, which is why 1.8 could neither place a block nor open a container.</remarks>
    public async Task PlaceBlockAsync(
        BlockPos position, Direction face, Vec3d cursor, Hand hand = Hand.Main,
        bool insideBlock = false, CancellationToken ct = default)
    {
        if (!_services.Wire.CanSendPlay(ItemPackets.Serverbound.UseItemOn))
        {
            await SendLegacyBlockPlaceAsync(nameof(PlaceBlockAsync), position, (int)face, cursor, ct).ConfigureAwait(false);
            return;
        }

        int seq = _sequences.Next();
        await _sink.SendAsync(
            new ServerboundUseItemOnPacket(
                (int)hand, position, (int)face,
                (float)cursor.X, (float)cursor.Y, (float)cursor.Z, insideBlock, WorldBorderHit: false, seq),
            ct).ConfigureAwait(false);
        if (HasSequences())
            await AwaitAckAsync(seq, ct).ConfigureAwait(false);

    }

    /// <summary>Uses the held item in the air.</summary>
    /// <remarks>Same era split as <see cref="PlaceBlockAsync"/>. 1.8 spells "use the held item in the air" as a <c>block_place</c> at the sentinel position (-1, -1, -1) with face 255. The 1.8 form carries only the held item stack.</remarks>
    public Task UseItemAsync(Hand hand = Hand.Main, CancellationToken ct = default)
    {
        if (!_services.Wire.CanSendPlay(ItemPackets.Serverbound.UseItem))
            return SendLegacyBlockPlaceAsync(nameof(UseItemAsync), LegacyUseItemPosition, LegacyUseItemFace, default, ct);

        return _sink.SendAsync(
            new ServerboundUseItemPacket((int)hand, _sequences.Next(), _services.Self.Yaw, _services.Self.Pitch),
            ct).AsTask();
    }

    /// <summary>The sentinel position 1.8 sends for "use the held item in the air".</summary>
    private static readonly BlockPos LegacyUseItemPosition = new(-1, -1, -1);

    /// <summary>The face value 1.8 uses to mean "not a block face"; vanilla branches on it explicitly.</summary>
    private const int LegacyUseItemFace = 255;

    /// <summary>Sends the pre-1.9 <c>block_place</c>. The held item comes from the client's inventory snapshot. The server acts on its own held-stack state and uses the packet copy for resynchronization, so a session without inventory state sends an empty stack and may receive a corrective slot update.</summary>
    private Task SendLegacyBlockPlaceAsync(string action, BlockPos position, int face, Vec3d cursor, CancellationToken ct)
    {
        Require(
            _services.Wire.CanSendPlay(ItemPackets.Serverbound.LegacyBlockPlace),
            action,
            ItemPackets.Serverbound.LegacyBlockPlace.Id,
            "Neither use_item_on nor the 1.8 block_place is sendable on this version.");

        return _sink.SendAsync(
            new ServerboundLegacyBlockPlacePacket(
                position, face, HeldItem(), LegacyCursor(cursor.X), LegacyCursor(cursor.Y), LegacyCursor(cursor.Z)),
            ct).AsTask();
    }

    /// <summary>The stack in the selected hotbar slot, or empty when inventory tracking is off. The 36-plus-held-slot arithmetic itself lives on <see cref="PlayerInventorySnapshot"/>, the one copy both this lookup and the off-loop read-model call through.</summary>
    private ItemStack HeldItem() => _services.State.Features.Inventory
        ? PlayerInventorySnapshot.ResolveHeldItem(_services.State.Inventory.PlayerSlots, _services.State.Self.HeldSlot)
        : ItemStack.Empty;

    /// <summary>Converts a 0..1 in-block cursor coordinate to the 1.8 wire byte. Vanilla writes <c>(int)(facing * 16.0F)</c> and reads <c>readUnsignedByte() / 16.0F</c>. The clamp keeps the value inside the record's documented 0..15 domain; the only input it changes is an exact 1.0, and 15/16 lands on the same side of every half-block test vanilla applies to this field.</summary>
    private static byte LegacyCursor(double value) => (byte)Math.Clamp((int)(value * 16.0), 0, 15);

    /// <summary>Attacks an entity (uses the 26.1+ dedicated attack packet where available).</summary>
    public Task AttackEntityAsync(int entityId, CancellationToken ct = default)
    {
        int attackId = _services.Wire.ServerboundPlay(Identifier.Minecraft("attack"));
        if (attackId >= 0)
            return _sink.SendAsync(new ServerboundAttackPacket(entityId), ct).AsTask();

        // Legacy: interact with action = attack, no hand.
        return _sink.SendAsync(
            new ServerboundInteractPacket(entityId, 1, Hand: null, InteractAt: null,
                UsingSecondaryAction: _services.Self.Sneaking),
            ct).AsTask();
    }

    /// <summary>Interacts with an entity (right-click).</summary>
    public Task InteractEntityAsync(int entityId, Hand hand = Hand.Main, CancellationToken ct = default)
        => _sink.SendAsync(
            new ServerboundInteractPacket(entityId, 0, (int)hand, InteractAt: null,
                UsingSecondaryAction: _services.Self.Sneaking),
            ct).AsTask();

    /// <summary>Swings the given arm.</summary>
    public Task SwingAsync(Hand hand = Hand.Main, CancellationToken ct = default)
        => _sink.SendAsync(new ServerboundSwingPacket((int)hand, HasHand: true), ct).AsTask();

    /// <summary>Updates the text of the sign at <paramref name="position"/> (the sign whose editor the server opened). The <paramref name="isFrontText"/> flag selects the front/back side on 1.20+ and is ignored on older versions. Lines beyond four are dropped and missing lines are sent empty.</summary>
    /// <exception cref="ActionNotSupportedException">The negotiated version cannot carry <c>sign_update</c>; ask <see cref="ClientActionCapabilities.CanUpdateSign"/> to branch instead of catching. Protocol 47 registers the identifier but leaves it a deliberate marker (its lines are JSON components), so this is one of the cases an identifier lookup gets wrong.</exception>
    public Task UpdateSignAsync(
        BlockPos position, IReadOnlyList<string> lines, bool isFrontText = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Require(
            _services.Capabilities.CanUpdateSign,
            nameof(UpdateSignAsync),
            UiPackets.Serverbound.SignUpdate.Id,
            "Protocol 47 carries the sign lines as JSON components, which this record does not model.");

        static string LineAt(IReadOnlyList<string> src, int i) => i < src.Count ? src[i] ?? string.Empty : string.Empty;
        var packet = new ServerboundSignUpdatePacket(
            position, isFrontText, LineAt(lines, 0), LineAt(lines, 1), LineAt(lines, 2), LineAt(lines, 3));
        return _sink.SendAsync(packet, ct).AsTask();
    }

    /// <summary>Spectator teleport to an entity by its UUID (the spectator "teleport to player" action).</summary>
    /// <exception cref="ActionNotSupportedException">The negotiated version cannot carry <c>teleport_to_entity</c>; ask <see cref="ClientActionCapabilities.CanSpectatorTeleport"/> to branch instead of catching.</exception>
    public Task SpectatorTeleportAsync(Guid targetUuid, CancellationToken ct = default)
    {
        Require(
            _services.Capabilities.CanSpectatorTeleport,
            nameof(SpectatorTeleportAsync),
            EntityPackets.Serverbound.TeleportToEntity.Id);

        return _sink.SendAsync(new ServerboundTeleportToEntityPacket(targetUuid), ct).AsTask();
    }

    /// <summary>Configures the command block at <paramref name="position"/>. The <paramref name="mode"/> is the block mode ordinal (0 sequence, 1 auto, 2 redstone). Requires 1.13+.</summary>
    /// <exception cref="ActionNotSupportedException">The negotiated version cannot carry <c>set_command_block</c>; ask <see cref="ClientActionCapabilities.CanUpdateCommandBlock"/> to branch instead of catching.</exception>
    public Task UpdateCommandBlockAsync(
        BlockPos position, string command, int mode = 2, bool trackOutput = true,
        bool conditional = false, bool automatic = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Require(
            _services.Capabilities.CanUpdateCommandBlock,
            nameof(UpdateCommandBlockAsync),
            WorldPackets.Serverbound.SetCommandBlock.Id);

        var packet = new ServerboundSetCommandBlockPacket(position, command, mode, trackOutput, conditional, automatic);
        return _sink.SendAsync(packet, ct).AsTask();
    }

    private void Require(bool capable, string action, Identifier packet, string? detail = null)
    {
        if (!capable)
            throw new ActionNotSupportedException(action, packet, _services.Version.Version.Protocol, detail);

    }

    // A bounded wait for the block-change ack; a silent server falls back to write-completion.
    private const double AckTimeoutSeconds = 5.0;

    private async Task AwaitAckAsync(int sequence, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(AckTimeoutSeconds));
        try
        {
            await _sequences.WaitForAsync(sequence, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out waiting for the acknowledgment: fall back to write-completion.
        }
    }

    private int? NextSequence()
        => _services.Version.Features.ChatSigning == "none" && !HasSequences() ? null : _sequences.Next();

    // The action sequence exists from 1.19 onward; use a play-registry probe as the era gate.
    private bool HasSequences()
        => _services.Wire.ServerboundPlay(Identifier.Minecraft("block_changed_ack")) >= 0
           || _services.Wire.ClientboundPlay(Identifier.Minecraft("block_changed_ack")) >= 0;
}
