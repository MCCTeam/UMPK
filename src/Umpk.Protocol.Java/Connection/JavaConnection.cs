using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Umpk.Protocol.Java.Crypto;
using Umpk.Protocol.Java.Transport;

namespace Umpk.Protocol.Java;

/// <summary>One Minecraft Java protocol connection over a duplex byte pipe. Owns framing (VarInt length prefix), compression (zlib, negotiated threshold), encryption (AES-CFB8), phase tracking, and ordered send/receive with real backpressure. Transport-agnostic: sockets, in-memory test pipes, and tunneled streams all enter through <see cref="IDuplexPipe"/>. It owns no application-visible threads, ticks nothing, and knows nothing of world/inventory state.</summary>
/// <remarks>A single read loop decodes frames and completes <see cref="ReceiveAsync"/> calls through a bounded channel; a full channel stalls the read loop (backpressure). The write path is serialized through a semaphore so <see cref="SendAsync"/> is safe from any thread. Everything is cancellable; there are no blocking public APIs. The packet-decode meaning of a frame is delegated to an <see cref="IFrameCodecBinding"/>; without one the connection runs in frame mode.</remarks>
public sealed class JavaConnection : IAsyncDisposable
{
    private const int DecodeFailureEvidenceLimit = 256;

    private readonly IDuplexPipe _pipe;

    private readonly JavaConnectionOptions _options;

    private readonly ILogger _logger;

    private readonly FrameReader _frameReader;

    private readonly FrameWriter _frameWriter;

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly Channel<InboundItem> _inbound;

    private readonly CancellationTokenSource _lifetimeCts = new();

    private readonly ArrayBufferWriter<byte> _encodeScratch = new(1024);

    private Task? _readLoop;

    private IFrameCodecBinding? _binding;

    private BundleAccumulator? _bundleAccumulator;

    private PacketDecodeFilter? _decodeFilter;

    private volatile int _closed; // 0 = open, 1 = closing/closed

    private CloseReason _closeReason = CloseReason.Local;

    private Exception? _faultReason;

    private int _unmodeledComponentReported; // 0 = not yet reported on this connection

    // Dispose-ownership gate: exactly one DisposeAsync caller (whichever flips this 0 -> 1 first) runs the actual teardown body; every other concurrent or later caller awaits _disposeCompletion instead of returning immediately. Without this a second caller could return from DisposeAsync
    // while the first was still tearing down - including while the read loop was still using
    // FrameReader's pooled scratch buffer, which _frameReader.Release() then returned to ArrayPool.Shared out from under it. See DisposeAsync.
    private int _disposeStarted;

    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Phase-transition pause/resume: when a terminal frame is decoded the read loop parks here until the consumer acknowledges the new phase via SetPhase.
    private TaskCompletionSource? _phaseGate;

    // Compression-enable pause/resume: when the set-compression frame is observed the read loop parks here until the consumer enables compression via EnableCompression, so the next frame (already the compressed format on a zero-latency pipe) is read with the compressed reader. Same mechanism as the phase gate, but for a byte-pipeline transform rather than a phase change.
    private TaskCompletionSource? _compressionGate;

    // Encryption-enable pause/resume: when the encryption-request frame is observed the read loop parks here until the consumer enables encryption via EnableEncryption, so the peer's subsequent encrypted bytes are not appended to the read buffer undecrypted. Same mechanism as the compression gate.
    private int _encrypted;

    private TaskCompletionSource? _encryptionGate;

    /// <summary>Creates a connection wrapping an established duplex byte pipe.</summary>
    public JavaConnection(IDuplexPipe pipe, JavaConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(options);

        _pipe = pipe;
        _options = options;
        _logger = options.Logger;
        _frameReader = new FrameReader(pipe.Input, options.MaxFrameLength);
        _frameWriter = new FrameWriter(pipe.Output);
        _inbound = options.InboundChannelCapacity > 0
            ? Channel.CreateBounded<InboundItem>(new BoundedChannelOptions(options.InboundChannelCapacity)
            {
                SingleReader = false,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            })
            : Channel.CreateUnbounded<InboundItem>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true,
            });
    }

    /// <summary>The connection's current protocol phase.</summary>
    public ProtocolPhase Phase { get; private set; } = ProtocolPhase.Handshake;

    /// <summary>The inbound flow this connection decodes. Set by role at bind time.</summary>
    public PacketFlow InboundFlow { get; private set; } = PacketFlow.Clientbound;

    /// <summary>Live byte/packet counters.</summary>
    public ConnectionStats Stats { get; } = new();

    /// <summary>The logger this connection was configured with. Internal so the static drivers in this assembly (<see cref="JavaClientLogin"/>) can report a fault against the connection that carries it, rather than threading a logger of their own through every call.</summary>
    internal ILogger Logger => _logger;

    /// <summary>True when compression has been enabled.</summary>
    public bool CompressionEnabled => _frameReader.CompressionEnabled;

    /// <summary>Raised for every frame observed in either direction, carrying the raw decrypted, decompressed bytes before decoding plus any decoded packet. Handlers must not retain the raw payload past the callback.</summary>
    public event Action<PacketObservation>? PacketObserved;

    /// <summary>Raised once when a mapped packet codec fails and the strict policy terminates the session. Subscriber exceptions are logged and contained. Evidence is capped at 256 bytes and is empty in handshake/login so authentication material cannot enter diagnostics.</summary>
    public event Action<PacketDecodeFailure>? PacketDecodeFailed;

    /// <summary>
    /// Raised for every frame this connection WRITES, once the bytes have reached the transport, carrying the same raw pre-compression, pre-encryption body the peer will decode plus the packet object that produced it (null for <see cref="SendFrameAsync"/>). Handlers must not retain the raw payload past the callback: the buffer is the connection's shared encode scratch and the next send overwrites it.
    /// <para>Deliberately a SEPARATE event from <see cref="PacketObserved"/>. A client subscribes the inbound event unconditionally (server-brand capture and plugin-channel dispatch both need it), so folding outbound into that event would make every send pay for an observation nobody asked for. With the split, a send with no outbound listener costs one null read of this field: no VarInt scan, no struct construction, no copy, no allocation.</para>
    /// </summary>
    public event Action<PacketObservation>? OutboundPacketObserved;

    /// <summary>Binds the codec seam and the inbound flow. A client binds <see cref="PacketFlow.Clientbound"/>; a server binds <see cref="PacketFlow.Serverbound"/>. May be called with a null binding to run in pure frame mode.</summary>
    public void BindCodec(IFrameCodecBinding? binding, PacketFlow inboundFlow)
    {
        _binding = binding;
        InboundFlow = inboundFlow;

        // A bound connection can assemble bundles: the accumulator buffers decoded packets between two bundle_delimiter frames and yields them atomically. Its delimiter detection is driven by the binding, so it stays inert for versions/bindings without a bundle delimiter.
        _bundleAccumulator = binding is not null ? new BundleAccumulator() : null;
    }

    /// <summary>Swaps the codec context (registry view + dynamic connection state) the bound codecs read during decode/encode. Valid only while inbound reading is paused at a phase transition; the connection is at such a pause between decoding a terminal packet and the consumer's <see cref="SetPhase"/> acknowledgment. Requires a <see cref="DescriptorFrameCodecBinding"/>.</summary>
    public void SetCodecState(Umpk.Game.Registries.RegistryAccess registries, Codecs.IConnectionCodecState state)
    {
        ArgumentNullException.ThrowIfNull(registries);
        ArgumentNullException.ThrowIfNull(state);
        if (_binding is not DescriptorFrameCodecBinding descriptorBinding)
            throw new InvalidOperationException(
                "SetCodecState requires a DescriptorFrameCodecBinding; none is attached.");

        descriptorBinding.SetCodecState(registries, state);
    }

    /// <summary>Acknowledges a phase transition (or forces one). If the read loop is parked awaiting the consumer's acknowledgment of a terminal packet, this releases it.</summary>
    public void SetPhase(ProtocolPhase phase)
    {
        Phase = phase;
        TaskCompletionSource? gate = Interlocked.Exchange(ref _phaseGate, null);
        gate?.TrySetResult();
    }

    /// <summary>Enables zlib compression with the negotiated threshold for both directions. If the read loop is parked at the set-compression frame boundary (armed when the <c>login_compression</c> frame was observed), this releases it so the next frame is read with the compressed reader.</summary>
    public void EnableCompression(int threshold)
    {
        if (threshold < 0)
            throw new ArgumentOutOfRangeException(nameof(threshold));

        _frameReader.EnableDecompression(threshold);
        _frameWriter.EnableCompression(threshold);

        TaskCompletionSource? gate = Interlocked.Exchange(ref _compressionGate, null);
        gate?.TrySetResult();
    }

    /// <summary>Enables AES-CFB8 encryption in both directions, seeded with the shared secret as the IV (the Minecraft convention). Call once, at the exact protocol byte position (the login helper does this). If the read loop is parked at the encryption-request frame boundary (armed when that frame was observed), this releases it so the peer's subsequent bytes are read through the decryptor.</summary>
    public void EnableEncryption(ReadOnlySpan<byte> sharedSecret)
    {
        _frameReader.EnableDecryption(AesCfb8.Create(sharedSecret));
        _frameWriter.EnableEncryption(AesCfb8.Create(sharedSecret));
        Volatile.Write(ref _encrypted, 1);

        TaskCompletionSource? gate = Interlocked.Exchange(ref _encryptionGate, null);
        gate?.TrySetResult();
    }

    /// <summary>Whether <see cref="EnableEncryption"/> has run on this connection, which is exactly whether the server sent an encryption request. That is the observable difference between an online-mode server and an offline-mode one, and it is load bearing for chat signing: an offline-mode server assigns an offline UUID (derived from the name), while a Mojang profile key's signature covers the account's real UUID, so announcing that key to an unencrypted server can only ever fail its validation.</summary>
    public bool IsEncrypted => Volatile.Read(ref _encrypted) != 0;

    /// <summary>Switches inbound delivery to frame mode with the given decode filter.</summary>
    public void SetDecodeFilter(PacketDecodeFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _decodeFilter = filter;
    }

    /// <summary>Starts the background read loop. Idempotent per connection.</summary>
    public void Start()
    {
        if (_readLoop is not null)
            return;

        _readLoop = Task.Run(() => ReadLoopAsync(_lifetimeCts.Token));
    }

    /// <summary>Sends a packet object, resolving its wire id and encoding via the bound codec. Ordered per connection. Requires a codec binding; use <see cref="SendFrameAsync"/> without one.</summary>
    public async ValueTask SendAsync(object packet, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(packet);
        IFrameCodecBinding binding = _binding
            ?? throw new InvalidOperationException("No codec binding is attached; use SendFrameAsync.");

        PacketFlow outbound = OutboundFlow;

        await AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfClosed();
            _encodeScratch.Clear();
            if (!binding.TryEncode(packet, Phase, outbound, _encodeScratch))
                throw new ProtocolViolationException(
                    $"Packet {packet.GetType().Name} is not valid to send in phase {Phase}.");

            int wire = await WriteEncodedFrameAsync(ct).ConfigureAwait(false);
            Stats.AddOutbound(wire);
            ConnectionDiagnostics.BytesOut.Add(wire);
            ConnectionDiagnostics.PacketsOut.Add(1);
            RaiseOutboundObservation(outbound, packet);
        }
        finally
        {
            ReleaseWriteLock();
        }
    }

    /// <summary>Sends a raw frame (wire id + payload) without codec encoding. Ordered per connection.</summary>
    public async ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        await AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfClosed();
            _encodeScratch.Clear();
            Span<byte> idSpan = _encodeScratch.GetSpan(VarInt.MaxBytes);
            int n = VarInt.Write(wireId, idSpan);
            _encodeScratch.Advance(n);
            payload.Span.CopyTo(_encodeScratch.GetSpan(payload.Length));
            _encodeScratch.Advance(payload.Length);

            int wire = await WriteEncodedFrameAsync(ct).ConfigureAwait(false);
            Stats.AddOutbound(wire);
            ConnectionDiagnostics.BytesOut.Add(wire);
            ConnectionDiagnostics.PacketsOut.Add(1);
            RaiseOutboundObservation(OutboundFlow, packet: null);
        }
        finally
        {
            ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Acquires <see cref="_writeLock"/> for one send, or throws the connection's typed close exception instead of ever letting a disposal race surface as <see cref="ObjectDisposedException"/>. This is the ONLY place <see cref="SendAsync"/> and <see cref="SendFrameAsync"/> touch the write lock, which makes it the single choke point for the class's documented "SendAsync is safe from any thread" contract: whatever caller raced <see cref="DisposeAsync"/> (a per-tick send using <see cref="CancellationToken.None"/>, a keep-alive echo, an explicit user send, two overlapping disposals), the failure comes out the same way, decided here instead of at each caller. On success the lock IS held and it is the caller's job to release it (via <see cref="ReleaseWriteLock"/>, in a <c>finally</c>) and to call <see cref="ThrowIfClosed"/> from inside that same <c>finally</c>-guarded region. This method only owns the acquire step, so a throw here never leaves the lock held.
    /// <para>Both <see cref="CloseAsync"/> and the read loop's <c>Complete</c> cancel <see cref="_lifetimeCts"/> as part of setting <see cref="_closed"/> (whichever of the two wins that transition), strictly before <see cref="DisposeAsync"/> ever disposes <c>_writeLock</c>. Waiting on (or, when <paramref name="ct"/> cannot be cancelled, directly on) <c>_lifetimeCts.Token</c> therefore turns the common case into an immediate, cooperative <see cref="ConnectionClosedException"/> instead of a wait that only resolves once dispose forces an <see cref="ObjectDisposedException"/> on whoever is still parked in <c>WaitAsync</c>. The catch below is a backstop for the residual timing window around disposal itself (not a claim that every interleaving is otherwise impossible): it is what actually converts any <see cref="ObjectDisposedException"/> that still reaches this method into the typed exception.</para>
    /// </summary>
    private async ValueTask AcquireWriteLockAsync(CancellationToken ct)
    {
        try
        {
            if (ct.CanBeCanceled)
            {
                using CancellationTokenSource linked =
                    CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
                await _writeLock.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            else
            {
                // The dominant call sites (per-tick sends, keep-alive echoes) pass CancellationToken.None, which can never itself fire, so there is nothing to link: waiting directly on _lifetimeCts.Token gets the same cooperative-cancellation behavior without allocating a CancellationTokenSource on every send.
                await _writeLock.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException)
        {
            // _writeLock (or _lifetimeCts, in the sliver of DisposeAsync between disposing the lock and disposing the cts) was disposed out from under this acquire. Nothing was acquired.
            throw CreateClosedException();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The caller's own token did not fire, so this was _lifetimeCts: the connection is closing/closed. Nothing was acquired. A caller-requested cancellation (ct.IsCancellationRequested) is left to propagate as OperationCanceledException, unchanged from before.
            throw CreateClosedException();
        }
    }

    /// <summary>Releases <see cref="_writeLock"/>, tolerating a concurrent dispose that already disposed it while this send held it. See <see cref="AcquireWriteLockAsync"/> for why this lives here and not at each call site.</summary>
    private void ReleaseWriteLock()
    {
        try
        {
            _writeLock.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposed while we held it. Whatever this send's own outcome was (it either already
            // threw, or the write actually reached the transport before teardown caught up) stands;
            // there is nothing left to release into.
        }
    }

    /// <summary>
    /// Writes the frame currently staged in <see cref="_encodeScratch"/> to the transport, translating the sibling half of the same disposal race <see cref="AcquireWriteLockAsync"/> guards against: a send that got past the post-acquire <see cref="ThrowIfClosed"/> check (because it read <c>_closed</c> as 0 a moment before <see cref="CloseAsync"/> flipped it) can still have <c>CloseAsync</c>'s <c>_pipe.Output.CompleteAsync()</c> land while <c>_frameWriter.WriteFrameAsync</c> is in flight to that same transport. On the in-memory pipe used by this project's tests that surfaces as <see cref="InvalidOperationException"/> ("Writing is not allowed after writer was completed."); a different <see cref="IDuplexPipe"/> could plausibly surface a subclass of it, most notably <see cref="ObjectDisposedException"/> - which the single <c>InvalidOperationException</c> clause below already catches, since it derives from it. Converted only when <see cref="_closed"/> is already 1.
    /// <para><c>_closed</c> means "closing or closed for any reason" - it is also set by the read loop's <c>Complete</c> on a READ-side-only fault (socket EOF, protocol violation, idle timeout), which completes only <c>_pipe.Input</c>, never <c>_pipe.Output</c>. So this gate can be open while the write path itself is completely healthy and undisposed, and in that state a genuine write-path bug (for example an <see cref="ArrayBufferWriter{T}"/> misuse in <c>_encodeScratch</c>) would be relabelled as a normal disconnect instead of surfacing as itself. That window is bounded but not instantaneous: it spans encode, compression, encryption and a `FlushAsync` that can block on backpressure. Deterministic framing faults remain visible on an open connection, where <c>_closed == 0</c> and this catch does not apply.</para>
    /// </summary>
    private async ValueTask<int> WriteEncodedFrameAsync(CancellationToken ct)
    {
        try
        {
            return await _frameWriter.WriteFrameAsync(_encodeScratch.WrittenMemory, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (_closed == 1)
        {
            throw CreateClosedException();
        }
    }

    /// <summary>Publishes an <see cref="OutboundPacketObserved"/> observation over the frame still sitting in <c>_encodeScratch</c> ([wire id VarInt][body]). Called under the write lock, so the scratch is stable for the duration of the callback. Returns immediately when nobody is listening, which is what keeps the send path free for a client with no raw-frame consumer.</summary>
    private void RaiseOutboundObservation(PacketFlow flow, object? packet)
    {
        Action<PacketObservation>? observers = OutboundPacketObserved;
        if (observers is null)
            return;

        ReadOnlyMemory<byte> written = _encodeScratch.WrittenMemory;
        if (!MemoryMarshal.TryGetArray(written, out ArraySegment<byte> segment)
            || segment.Array is null
            || !VarInt.TryRead(written.Span, out int wireId, out int idBytes))
            return;

        observers(new PacketObservation(
            flow, Phase, wireId, segment.Array, segment.Offset + idBytes, segment.Count - idBytes, packet));
    }

    /// <summary>The flow this connection WRITES, i.e. the opposite of <see cref="InboundFlow"/>.</summary>
    private PacketFlow OutboundFlow
        => InboundFlow == PacketFlow.Clientbound ? PacketFlow.Serverbound : PacketFlow.Clientbound;

    /// <summary>Receives the next inbound item. Throws <see cref="ConnectionClosedException"/> at end.</summary>
    public async ValueTask<InboundItem> ReceiveAsync(CancellationToken ct)
    {
        try
        {
            return await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw CreateClosedException();
        }
    }

    /// <summary>Streams inbound items until the connection closes.</summary>
    public async IAsyncEnumerable<InboundItem> ReceiveAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            InboundItem item;
            try
            {
                item = await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                yield break;
            }

            yield return item;
        }
    }

    /// <summary>Streams raw frames until the connection closes (frame mode for relays).</summary>
    public async IAsyncEnumerable<InboundFrame> ReceiveFramesAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (InboundItem item in ReceiveAllAsync(ct).ConfigureAwait(false))
            yield return item.Frame;

    }

    /// <summary>Marks the connection closed and arms the cooperative-cancellation path <see cref="AcquireWriteLockAsync"/> links against, atomically: the two must never be separable, because a setter of <see cref="_closed"/> that forgets to arm <see cref="_lifetimeCts"/> can leave a sender parked forever during read-loop-first closure. Centralizing both transitions here prevents either close path from omitting cancellation. <see cref="_closed"/> must never be assigned anywhere else. Returns <see langword="true"/> only for the caller that actually won the transition from open to closed; false for every later or concurrent caller.</summary>
    private bool TryMarkClosed(CloseReason reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return false;

        _closeReason = reason;
        try
        {
            _lifetimeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a concurrent/prior DisposeAsync; nothing left to arm.
        }

        return true;
    }

    /// <summary>Closes the connection with a reason, completing pending receives.</summary>
    public async ValueTask CloseAsync(CloseReason reason, CancellationToken ct)
    {
        if (!TryMarkClosed(reason))
            return;

        try
        {
            await _pipe.Output.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Error completing output pipe during close.");
        }

        _inbound.Writer.TryComplete();
        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // caller's ct cancelled the wait; the loop still winds down via lifetime cts
            }
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                FramePayload? maybe = await ReadFrameWithTimeoutAsync(ct).ConfigureAwait(false);
                if (maybe is not FramePayload payload)
                {
                    Complete(CloseReason.SocketEof, null);
                    return;
                }

                await HandleFrameAsync(payload, ct).ConfigureAwait(false);

                // Compression-enable pause: park at the set-compression frame boundary until the consumer calls EnableCompression, so the next frame is read with the compressed reader. Must precede the next ReadFrameAsync (which extracts the next frame).
                TaskCompletionSource? compressionGate = Volatile.Read(ref _compressionGate);
                if (compressionGate is not null)
                    await compressionGate.Task.WaitAsync(ct).ConfigureAwait(false);

                // Encryption-enable pause: park at the encryption-request frame boundary until the consumer calls EnableEncryption, so the peer's subsequent encrypted bytes are read with the decryptor rather than appended to the buffer raw. Same mechanism as above.
                TaskCompletionSource? encryptionGate = Volatile.Read(ref _encryptionGate);
                if (encryptionGate is not null)
                    await encryptionGate.Task.WaitAsync(ct).ConfigureAwait(false);

                // Phase-transition pause: park until the consumer acknowledges via SetPhase.
                TaskCompletionSource? gate = Volatile.Read(ref _phaseGate);
                if (gate is not null)
                    await gate.Task.WaitAsync(ct).ConfigureAwait(false);

            }

            Complete(_closeReason, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Complete(_closeReason, null);
        }
        catch (ConnectionClosedException ex)
        {
            Complete(ex.Reason, ex);
        }
        catch (ProtocolViolationException ex)
        {
            ConnectionDiagnostics.DecodeErrors.Add(1);
            Complete(CloseReason.ProtocolViolation, ex);
        }
        catch (Exception ex)
        {
            Complete(CloseReason.ProtocolViolation, ex);
        }
    }

    private async ValueTask<FramePayload?> ReadFrameWithTimeoutAsync(CancellationToken ct)
    {
        TimeSpan idle = _options.ReadIdleTimeout;
        if (idle <= TimeSpan.Zero)
            return await _frameReader.ReadFrameAsync(ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(idle);
        try
        {
            return await _frameReader.ReadFrameAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _closeReason = CloseReason.IdleTimeout;
            throw new ConnectionClosedException(CloseReason.IdleTimeout, "Read idle timeout elapsed.");
        }
    }

    private async ValueTask HandleFrameAsync(FramePayload payload, CancellationToken ct)
    {
        // payload.Buffer holds wire-id VarInt + fields.
        ReadOnlySpan<byte> full = payload.Span;
        int wireId = ReadWireId(full, out int idBytes);
        int bodyLen = payload.Length - idBytes;

        // The frame crosses a bounded channel and may sit buffered behind other frames, so its payload buffer must outlive the read loop's pooled decode buffer. Copy the body (after the wire id) into a right-sized array owned by the delivered frame. The doc's pooled linearization requirement is met upstream in FrameReader; here correctness under buffering wins over reusing a single recycled buffer.
        byte[] live = bodyLen == 0 ? [] : new byte[bodyLen];
        full[idBytes..].CopyTo(live);
        FrameReader.ReturnFrame(payload.Buffer);

        Stats.AddInbound(payload.WireFrameLength);
        Stats.RecordInboundCompression(payload.WireFrameLength, payload.Length);
        ConnectionDiagnostics.BytesIn.Add(payload.WireFrameLength);
        ConnectionDiagnostics.PacketsIn.Add(1);

        // Decode (item mode) or select-decode (frame mode), applying the decode-failure policy when a mapped codec throws mid-decode.
        object? decoded = null;
        bool known = false;
        bool skip = false;
        bool resolved = false;
        ResolvedFrame resolvedFrame = default;

        if (_binding is not null)
        {
            // One resolution, one frame context. The gates below read the frame's roles off the entry this lookup already returned, so a delivered frame costs one table hit instead of six. Frame mode still resolves: it decodes nothing, but the compression and encryption gates are identity checks and must still fire (the login driver reads at the frame level).
            bool decodeThisFrame =
                _decodeFilter is null || _decodeFilter.ShouldDecode(Phase, InboundFlow, wireId);
            try
            {
                if (FrameDispatchSequence.ResolveAndDecode(
                        _binding, Phase, InboundFlow, wireId, live.AsSpan(0, bodyLen), decodeThisFrame,
                        out resolved, out resolvedFrame, out object packet))
                    decoded = packet;

                // A frame the filter passed over is known by construction, so the unknown policy is skipped.
                known = decoded is not null || !decodeThisFrame;
            }
            catch (UnmodeledItemComponentException ex)
            {
                // The ONE decode fault that costs a packet instead of the session, and the only catch of this type anywhere. An item component patch named a component this era's table knows but UMPK does not model; compact component payloads carry no length prefix, so the rest of this frame is unreadable. The frame is length-delimited at the transport, though, so the packet is the right recovery boundary: drop it and keep the session.
                //
                // This deliberately ignores DecodeFailurePolicy. That policy governs FRAMING strictness, and this case is provably not a framing fault: the offending id came out of the era's own component table. An id outside that table never reaches here (it raises ProtocolViolationException) and still kills the connection, so a real desync cannot hide behind this branch.
                ConnectionDiagnostics.UnmodeledComponents.Add(1);
                ReportUnmodeledComponent(ex, wireId);
                known = true;
                skip = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A mapped codec threw (a malformed field, or BoundPacketCodec.Decode's frame-exact trailing-byte check). Apply DecodeFailurePolicy instead of always killing the session.
                ConnectionDiagnostics.DecodeErrors.Add(1);
                switch (_options.DecodeFailurePolicy)
                {
                    case DecodeFailureMode.SkipFrameAndReport:
                        _logger.LogDebug(ex, "Skipping undecodable frame (wire id 0x{WireId:X2}, phase {Phase}).", wireId, Phase);
                        known = true;
                        skip = true;
                        break;
                    case DecodeFailureMode.ForwardVerbatim:
                        _logger.LogDebug(ex, "Forwarding undecodable frame verbatim (wire id 0x{WireId:X2}, phase {Phase}).", wireId, Phase);
                        decoded = new UnknownPacket(wireId, live.AsMemory(0, bodyLen));
                        known = true;
                        break;
                    default: // FailConnection: preserve the strict behavior.
                        ReportFatalDecodeFailure(wireId, live.AsSpan(0, bodyLen), ex, resolvedFrame.Codec);
                        throw;
                }
            }
        }

        var frame = new InboundFrame(wireId, live, bodyLen);

        Action<PacketObservation>? observers = PacketObserved;
        observers?.Invoke(new PacketObservation(InboundFlow, Phase, wireId, live, 0, bodyLen, decoded));

        if (!known)
        {
            switch (_options.UnknownPacketPolicy)
            {
                case UnknownPacketPolicy.Throw when _decodeFilter is null:
                    throw new ProtocolViolationException($"Unmapped wire id 0x{wireId:X2} in phase {Phase}.")
                    {
                        WireId = wireId,
                    };
                case UnknownPacketPolicy.Skip:
                    return;
                default:
                    // Preserve / frame-mode: deliver the raw frame with no decoded packet.
                    break;
            }
        }

        // SkipFrameAndReport: the frame was observed and counted; drop it without delivery.
        if (skip)
            return;

        // Bundle accumulation (1.19.4+): between two bundle_delimiter frames, buffer the packets and deliver them atomically as one PacketBundle. Item mode only; a proxy relays frames verbatim and never assembles bundles. Delimiters are never terminal/compression/encryption points, so bundle frames bypass the pause gates below.
        //
        // A frame with no decoded packet (a registered marker under UnknownPacketPolicy.Preserve, or an unmapped wire id) is still a packet in the stream, and vanilla's bundler draws no such distinction: PacketBundlePacker buffers whatever arrives between the delimiters, and the client holds a codec for all of it. So an OPEN bundle takes markers too, as UnknownPacket carrying the real wire id and body. Outside a bundle, a marker is delivered as a frame-only item.
        if (_bundleAccumulator is not null
            && _decodeFilter is null
            && (decoded is not null || _bundleAccumulator.IsAccumulating))
        {
            FrameDispatchSequence.ReadBundleIdentity(
                _binding!, resolved, in resolvedFrame, Phase, InboundFlow, wireId, live.AsSpan(0, bodyLen),
                out bool isDelimiter, out bool isTerminal);

            if (_bundleAccumulator.IsAccumulating && !isDelimiter && isTerminal)
            {
                // Vanilla PacketBundlePacker.verifyNonTerminalPacket: a terminal packet inside a bundle is a protocol error ("Terminal message received in bundle").
                throw new ProtocolViolationException(
                    $"Terminal packet (wire id 0x{wireId:X2}) received inside a bundle.")
                {
                    WireId = wireId,
                };
            }

            // The frame travels into the bundle so each bundled packet keeps the wire id and byte count it arrived with: the bundle item itself has no frame, and a consumer that publishes frame identity would otherwise have to make one up.
            object bundled = decoded ?? new UnknownPacket(wireId, live.AsMemory(0, bodyLen));
            switch (_bundleAccumulator.Offer(bundled, isDelimiter, in frame, out PacketBundle? bundle))
            {
                case BundleFeed.Opened:
                case BundleFeed.Buffered:
                    // Withhold: the delimiter opens the bundle; interior packets buffer until it closes.
                    return;
                case BundleFeed.Closed:
                    await _inbound.Writer.WriteAsync(new InboundItem(bundle), ct).ConfigureAwait(false);
                    return;
                case BundleFeed.PassThrough:
                default:
                    break; // not part of a bundle; fall through to normal delivery
            }
        }

        // Arm the pause gates before delivery so the read loop parks after this frame, before reading the next one. These are identity checks against the descriptor, so they fire even in frame mode where login_compression / the encryption request are delivered without being decoded (the login driver reads at the frame level).
        if (_binding is not null)
        {
            FrameDispatchSequence.ReadGates(
                _binding, resolved, in resolvedFrame, Phase, InboundFlow, wireId, live.AsSpan(0, bodyLen),
                decoded is not null,
                out bool compressionEnablePoint, out bool encryptionEnablePoint, out bool terminal);

            // Set-compression frame: park until the consumer enables compression.
            if (compressionEnablePoint)
                Volatile.Write(ref _compressionGate,
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            // Encryption-request frame: park until the consumer enables encryption.
            if (encryptionEnablePoint)
                Volatile.Write(ref _encryptionGate,
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            // Terminal-packet phase transition (only meaningful once the frame decodes to a packet).
            if (terminal)
                Volatile.Write(ref _phaseGate,
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        }

        await _inbound.Writer.WriteAsync(new InboundItem(decoded, frame), ct).ConfigureAwait(false);
    }

    private void Complete(CloseReason reason, Exception? fault)
    {
        // Complete() is the read loop's close trigger for socket EOF, protocol violations, idle timeouts, and normal loop exit. Routing it and CloseAsync through TryMarkClosed ensures that whichever path wins also cancels _lifetimeCts, releasing any sender waiting on the phase gate.
        TryMarkClosed(reason);

        _faultReason ??= fault;

        // A bundle open at close is abandoned, not delivered partially: its buffered packets are discarded so atomicity holds (an incomplete bundle is never surfaced). Note it for diagnostics.
        if (_bundleAccumulator is { IsAccumulating: true })
            _logger.LogDebug("Connection closed mid-bundle ({Reason}); buffered bundle packets discarded.", reason);

        _inbound.Writer.TryComplete(fault);
        try
        {
            _pipe.Input.Complete(fault);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error completing input pipe.");
        }

        if (fault is not null)
            _logger.LogDebug(fault, "Connection read loop terminated with a fault ({Reason}).", reason);

    }

    /// <summary>Reports one fatal mapped-decode boundary. Manual login/configuration drivers use the same method because their frame filter intentionally moves decoding out of <see cref="HandleFrameAsync"/>.</summary>
    internal void ReportFatalDecodeFailure(
        int wireId,
        ReadOnlySpan<byte> payload,
        Exception exception,
        BoundPacketCodec? codec = null,
        bool suppressEvidence = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        bool sensitivePhase = Phase is ProtocolPhase.Handshake or ProtocolPhase.Login;
        int retained = suppressEvidence || sensitivePhase
            ? 0
            : Math.Min(payload.Length, DecodeFailureEvidenceLimit);
        byte[] evidence = retained == 0 ? [] : payload[..retained].ToArray();
        int? protocol = (_binding as DescriptorFrameCodecBinding)?.Descriptor.Version.Protocol;

        var failure = new PacketDecodeFailure(
            protocol,
            Phase,
            InboundFlow,
            wireId,
            codec?.Type.Id,
            codec?.CodecIdentity,
            payload.Length,
            evidence,
            retained < payload.Length,
            exception);

        _logger.LogWarning(
            exception,
            "Fatal packet decode failure (protocol {Protocol}, phase {Phase}, flow {Flow}, wire id 0x{WireId:X2}, packet {PacketId}, codec {CodecIdentity}, payload length {PayloadLength}, retained {EvidenceLength}).",
            protocol?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown",
            Phase,
            InboundFlow,
            wireId,
            codec?.Type.Id.ToString() ?? "unknown",
            codec?.CodecIdentity ?? "unknown",
            payload.Length,
            evidence.Length);

        Action<PacketDecodeFailure>? handlers = PacketDecodeFailed;
        if (handlers is null)
            return;

        foreach (Action<PacketDecodeFailure> handler in handlers.GetInvocationList().Cast<Action<PacketDecodeFailure>>())
        {
            try
            {
                handler(failure);
            }
            catch (Exception subscriberError)
            {
                _logger.LogError(
                    subscriberError,
                    "A PacketDecodeFailed handler threw for wire id 0x{WireId:X2}.",
                    wireId);
            }
        }
    }

    private void ThrowIfClosed()
    {
        if (_closed == 1)
            throw CreateClosedException();

    }

    private ConnectionClosedException CreateClosedException() =>
        _faultReason is not null
            ? new ConnectionClosedException(_closeReason, _faultReason.Message, _faultReason)
            : new ConnectionClosedException(_closeReason);

    /// <summary>Surfaces a dropped packet caused by an unmodeled item component: once per connection at Warning (so an operator sees that inventory data is incomplete without the log filling up), and every occurrence at Debug. The <c>umpk.connection.unmodeled_components</c> counter carries the volume.</summary>
    private void ReportUnmodeledComponent(UnmodeledItemComponentException ex, int wireId)
    {
        if (Interlocked.Exchange(ref _unmodeledComponentReported, 1) == 0)
        {
            _logger.LogWarning(
                ex,
                "Dropped packet (wire id 0x{WireId:X2}, phase {Phase}): item component '{ComponentId}' is not modeled, and a compact component payload cannot be skipped. The session continues; further occurrences log at Debug.",
                wireId,
                Phase,
                ex.ComponentId);
            return;
        }

        _logger.LogDebug(
            ex,
            "Dropped packet (wire id 0x{WireId:X2}, phase {Phase}): unmodeled item component '{ComponentId}'.",
            wireId,
            Phase,
            ex.ComponentId);
    }

    internal static int ReadWireId(ReadOnlySpan<byte> span, out int bytesRead) =>
        VarInt.Read(span, VarInt.MaxBytes, out int value, out bytesRead) switch
        {
            VarIntStatus.Ok => value,
            VarIntStatus.TooLong => throw new ProtocolViolationException("Wire id VarInt is too long."),
            _ => throw new ProtocolViolationException("Frame ended before the wire id VarInt terminated."),
        };

    /// <summary>
    /// Tears the connection down. Safe to call more than once, and safe to call concurrently with itself: exactly one caller (whichever wins <see cref="_disposeStarted"/>) runs the teardown body below; every other concurrent or later <see cref="DisposeAsync"/> caller awaits that SAME completion instead of returning immediately. It is also safe against a concurrent, independent <see cref="CloseAsync"/> call because disposal joins the read loop unconditionally.
    /// <para>The hazard both of these guard against: <c>_frameReader.Release()</c> returns FrameReader's pooled scratch buffer to <see cref="ArrayPool{T}.Shared"/>, and the read loop is the only other thing that ever touches it. If the release ran while the read loop was still using that buffer - reachable whenever whoever raced this call already won the <see cref="_closed"/> transition (a concurrent <c>DisposeAsync</c>, an independent <c>CloseAsync</c> call, or the read loop's own <c>Complete</c>), because then <see cref="CloseAsync"/>'s own internal wait for <c>_readLoop</c> is skipped entirely by its `_closed` short-circuit - that is a use-after-return on pooled memory, not merely a tidiness issue. The unconditional join below makes completion of the read loop a precondition for releasing the buffer regardless of which close path won.</para>
    /// <para>Liveness trade, deliberate: the join below has no timeout and does not observe cancellation, so a read loop that never finishes blocks every <c>DisposeAsync</c> caller. This preserves the requirement that pooled memory cannot be released while the read loop may still use it.</para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
        {
            await new ValueTask(_disposeCompletion.Task).ConfigureAwait(false);
            return;
        }

        try
        {
            try
            {
                await CloseAsync(_closeReason, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error during connection dispose.");
            }

            // Unconditional, regardless of whether the CloseAsync call above actually won the _closed transition: if someone else already closed the connection first - a concurrent explicit CloseAsync(), or the read loop's own Complete() - the call above short-circuits immediately via TryMarkClosed and never waits for _readLoop at all. Joining it here directly, unconditionally, makes "the read loop has finished" a real precondition of the pooled-buffer release below no matter who closed the connection first.
            if (_readLoop is not null)
            {
                try
                {
                    await _readLoop.ConfigureAwait(false);
                }
                catch
                {
                    // ReadLoopAsync catches and logs its own faults internally (see Complete's caller sites) and does not rethrow; this join exists purely to sequence the release below after the loop is actually done, not to observe its outcome.
                }
            }

            _frameReader.Release();

            // IDuplexPipe itself has no lifetime interface. Socket-backed factories return an owning disposable adapter, while in-memory/test pipe halves do not. Complete preserves the transport-agnostic close semantics; final disposal closes only a pipe that explicitly advertises resource ownership, which sends FIN/EOF for NetworkStream-backed sockets.
            try
            {
                if (_pipe is IAsyncDisposable asyncPipe)
                    await asyncPipe.DisposeAsync().ConfigureAwait(false);

                else if (_pipe is IDisposable pipe)
                    pipe.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing the owned duplex transport.");
            }

            // _writeLock is not disposed because SemaphoreSlim.Dispose() does not signal pending WaitAsync waiters. The cancellation linkage in AcquireWriteLockAsync normally wakes a parked sender first, but wake-up and disposal race, and losing that race parks the sender forever. A parked sender can therefore remain in WaitingForActivation indefinitely.
            //
            // Disposing a SemaphoreSlim is only required to release the lazily-allocated AvailableWaitHandle. This connection never accesses that property, so no wait handle is allocated and disposal would only risk stranding senders racing teardown.
            //
            _lifetimeCts.Dispose();
        }
        finally
        {
            // Every waiter observes teardown as done regardless of whether the body above completed cleanly; an exception here already propagates to this (owning) caller, which is the same visibility a single, non-concurrent DisposeAsync call always had.
            _disposeCompletion.TrySetResult();
        }
    }
}
