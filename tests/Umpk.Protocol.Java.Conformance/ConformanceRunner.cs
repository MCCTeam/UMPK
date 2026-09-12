using System.Buffers;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit.Corpus;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>
/// The per-frame conformance check: for every recorded frame, resolve the protocol descriptor's codec for its (phase, flow, wire id) and assert one of:
/// <list type="bullet">
/// <item>implemented codec: decode succeeds, consumes the frame exactly, and re-encodes byte-identical
/// to the recorded body (proxy-grade fidelity);</item>
/// <item>marker (registered-but-unimplemented): the frame is preserved verbatim (ForwardVerbatim), so
/// no bytes are lost;</item>
/// <item>unregistered wire id: a conformance failure (a corpus frame no descriptor knows about).</item>
/// </list>
/// The recorded body predates decoding, so a codec bug cannot alter the expected bytes.
/// </summary>
public static class ConformanceRunner
{
    /// <summary>The outcome bucket for one frame.</summary>
    public enum Outcome
    {
        /// <summary>Implemented codec decoded and re-encoded byte-identically (the goal).</summary>
        ByteIdentical,

        /// <summary>Registered marker; frame preserved verbatim (ForwardVerbatim). Reported, not failed.</summary>
        MarkerVerbatim,

        /// <summary>An implemented codec threw while decoding the real frame, or decoded it but threw while re-encoding it. Both are hard failures: an implemented codec that cannot round-trip a real recorded frame is a fidelity defect, whether it throws or produces different bytes. The bucket is retained (distinct from <see cref="ReEncodeMismatch"/>) only so the failure message can say the codec threw rather than mismatched.</summary>
        DecodeFailed,

        /// <summary>An implemented codec decoded but re-encoded to different bytes: a real fidelity corruption. This is the hard failure the suite exists to catch.</summary>
        ReEncodeMismatch,
    }

    /// <summary>The detailed result of checking one frame.</summary>
    public readonly record struct FrameResult(Outcome Outcome, string? Detail);

    /// <summary>Checks a single frame against a descriptor. Unregistered wire ids throw <see cref="ConformanceViolation"/> (a corpus frame no descriptor knows about is always a failure). Decode throws and re-encode mismatches are returned as outcomes so the caller can apply decode leniency (report decode-not-yet-robust, hard-fail true byte mismatches).</summary>
    /// <param name="descriptor">The recording protocol's descriptor.</param>
    /// <param name="frame">The recorded frame.</param>
    /// <param name="loginTerminated">True once a clientbound <c>login_finished</c> (LoginSuccess) has been observed earlier in the same recording. Pre-1.20.2 protocols (e.g. 1.8) have no configuration phase and no terminal-packet gate, so a real client flips its phase machine to Play the instant LoginSuccess arrives, and every following clientbound frame is Play. The recorder can still label those frames Login (its <c>Connection.Phase</c> lags). When this flag is set, a Login-labeled clientbound frame is resolved against the Play registry first, which is the phase the frame truly belongs to. This matters when a wire id is registered in <em>both</em> phases (0x01 login hello vs Play JoinGame; 0x03 login_compression vs Play time_update): without the latch the frame would be mis-decoded with the Login codec that a live client would never apply post-LoginSuccess.</param>
    /// <param name="context">The codec context to decode and re-encode with. Defaults to <see cref="PacketCodecContext.Registryless"/>, which is correct for codecs that never touch a registry holder. Callers that have the capture's protocol should pass a context over that protocol's real static registries: registry-resolving codecs (item stacks in particular) decode in a degraded mode against an empty registry, and a frame that carries a real item id fails there while a live session, which always has registries, decodes it fine.</param>
    public static FrameResult CheckFrame(
        ProtocolDescriptor descriptor,
        RecordedFrame frame,
        bool loginTerminated = false,
        PacketCodecContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(frame);
        context ??= PacketCodecContext.Registryless;

        ProtocolPhase phase = CorpusEnumMapping.ToProtocolPhase(frame.Phase);
        PacketFlow flow = CorpusEnumMapping.ToFlow(frame.Direction);

        // Both the post-LoginSuccess latch and the Login->Play fallback below are 1.8-era accommodations. Their whole justification is that pre-1.20.2 versions have no configuration phase and no terminal-packet gate on login_finished, so the recorder's Connection.Phase can lag into Play
        // while frames are still labeled Login. A version that DOES have a configuration phase gates the
        // login->config->play transitions, so a Login-labeled clientbound frame is authoritative: applying either leniency there could launder a genuinely mislabeled or unregistered frame past the gate. Detecting the absence of a configuration phase is the actual 1.8 property, so gate on that (it restricts both mechanisms to the protocol-47 corpora, per their documented rationale) rather than on a protocol-number comparison.
        bool hasConfigurationPhase =
            descriptor.TryGetRegistry(ProtocolPhase.Configuration, PacketFlow.Clientbound, out _) ||
            descriptor.TryGetRegistry(ProtocolPhase.Configuration, PacketFlow.Serverbound, out _);

        // Post-LoginSuccess phase-lag correction: a Login-labeled clientbound frame recorded after the login flow terminated is really a Play frame. Prefer the Play registry so an id that collides between Login and Play resolves to the codec the live client's phase machine would use.
        if (!hasConfigurationPhase &&
            loginTerminated &&
            phase == ProtocolPhase.Login &&
            flow == PacketFlow.Clientbound &&
            descriptor.TryGetRegistry(ProtocolPhase.Play, flow, out PhaseRegistry postLoginPlay) &&
            postLoginPlay.TryGetInbound(frame.WireId, out BoundPacketCodec postLoginCodec))
            return CheckResolved(postLoginCodec, frame, ProtocolPhase.Play, flow, context);

        if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
            throw new ConformanceViolation(
                $"No packet registry for {phase}/{flow} (frame seq {frame.Sequence}, wire 0x{frame.WireId:X2}).");

        if (!registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec))
        {
            // Login->Play transition-window tolerance. Pre-1.20.2 (e.g. 1.8) has no configuration phase and no terminal-packet gate on login_finished, so the transport's read loop can observe the first burst of Play packets while Connection.Phase still reads Login. The recorder faithfully labels the frame with that lagging phase; the *bytes* are correct expected wire bytes. If a Login-labeled frame is unregistered in Login but resolves cleanly in Play, treat it as a Play frame. A wire id unknown in BOTH phases still fails below, so a genuinely unregistered packet is never laundered through. Gated to versions without a configuration phase: a version with the config-phase terminal gate has no phase-lag window, so a Login-labeled frame that collides into a Play id there must be flagged, not silently re-resolved.
            if (!hasConfigurationPhase &&
                phase == ProtocolPhase.Login &&
                descriptor.TryGetRegistry(ProtocolPhase.Play, flow, out PhaseRegistry playRegistry) &&
                playRegistry.TryGetInbound(frame.WireId, out BoundPacketCodec playCodec))
            {
                phase = ProtocolPhase.Play;
                registry = playRegistry;
                codec = playCodec;
            }
            else
            {
                // A corpus frame whose wire id is not registered at all: fail (packets appearing in corpora but not registered are always a failure).
                throw new ConformanceViolation(
                    $"Wire id 0x{frame.WireId:X2} in {phase}/{flow} is not registered (frame seq {frame.Sequence}).");
            }
        }

        return CheckResolved(codec, frame, phase, flow, context);
    }

    /// <summary>Runs the decode / re-encode / byte-identity contract for a frame whose (phase, flow, codec) have already been resolved. Decode throws and re-encode throws surface as <see cref="Outcome.DecodeFailed"/> and a decode that re-encodes to different bytes as <see cref="Outcome.ReEncodeMismatch"/>; the caller hard-fails on both.</summary>
    private static FrameResult CheckResolved(
        BoundPacketCodec codec, RecordedFrame frame, ProtocolPhase phase, PacketFlow flow, PacketCodecContext context)
    {
        if (!codec.IsImplemented)
        {
            // Marker: ForwardVerbatim preserves the bytes. The raw body is the payload the proxy would forward unchanged; there is nothing to re-encode.
            return new FrameResult(Outcome.MarkerVerbatim, null);
        }

        object packet;
        try
        {
            // Implemented: decode with exact-consumption enforcement inside Decode.
            packet = codec.Decode(frame.Body, context);
        }
        catch (Exception ex) when (ex is not ConformanceViolation)
        {
            return new FrameResult(
                Outcome.DecodeFailed,
                $"{codec.Type.Id} (wire 0x{frame.WireId:X2}, {phase}/{flow}, seq {frame.Sequence}) decode threw: " +
                $"{ex.GetType().Name}: {ex.Message}");
        }

        var buffer = new ArrayBufferWriter<byte>(frame.Body.Length + 8);
        var writer = new PacketWriter(buffer);
        try
        {
            codec.Encode(ref writer, packet, context);
        }
        catch (Exception ex) when (ex is not ConformanceViolation)
        {
            return new FrameResult(
                Outcome.DecodeFailed,
                $"{codec.Type.Id} (wire 0x{frame.WireId:X2}, seq {frame.Sequence}) re-encode threw: " +
                $"{ex.GetType().Name}: {ex.Message}");
        }

        if (!buffer.WrittenSpan.SequenceEqual(frame.Body))
            return new FrameResult(
                Outcome.ReEncodeMismatch,
                $"Re-encode of {codec.Type.Id} (wire 0x{frame.WireId:X2}, {phase}/{flow}, seq {frame.Sequence}) " +
                $"differs: recorded {frame.Body.Length} bytes, re-encoded {buffer.WrittenCount} bytes.");

        return new FrameResult(Outcome.ByteIdentical, null);
    }
}

/// <summary>Raised when a corpus frame fails the conformance contract.</summary>
public sealed class ConformanceViolation : Exception
{
    /// <summary>Creates the violation with a message.</summary>
    public ConformanceViolation(string message)
        : base(message)
    {
    }
}
