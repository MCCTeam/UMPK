namespace Umpk.Protocol.Java;

/// <summary>The binding queries the read loop makes for one inbound frame, in the order it makes them.</summary>
/// <remarks>
/// <para><see cref="JavaConnection"/>'s frame handler interleaves these sites with delivery, the unknown/decode-failure policies and a channel write, so it calls them one site at a time and keeps the surrounding conditions. <see cref="Run"/> composes the same calls in the same order for a caller that has no live connection to feed. Composing them here rather than in the caller is what keeps a measurement on the path it claims to measure: the arms share these bodies instead of a copy that can drift.</para>
/// <para>A binding that resolves a frame answers every later question from the resolved entry, so the frame costs one lookup and one <see cref="FrameDecodeContext"/>. An unresolved binding uses the predicate path and constructs context only at the sites that need it.</para>
/// </remarks>
internal static class FrameDispatchSequence
{
    /// <summary>Resolves the frame and, when <paramref name="decode"/> is set, decodes it under the same context. Frame mode passes false: the resolution still happens, because the compression and encryption gates are identity checks that fire whether or not anything decoded.</summary>
    /// <returns>True when the frame produced a packet.</returns>
    internal static bool ResolveAndDecode(
        IFrameCodecBinding binding,
        ProtocolPhase phase,
        PacketFlow flow,
        int wireId,
        ReadOnlySpan<byte> body,
        bool decode,
        out bool resolved,
        out ResolvedFrame frame,
        out object packet)
    {
        var context = new FrameDecodeContext(phase, flow, wireId, body);
        resolved = binding.TryResolve(in context, out frame);
        if (!decode)
        {
            packet = null!;
            return false;
        }

        return resolved
            ? binding.TryDecode(in frame, in context, out packet)
            : binding.TryDecode(in context, out packet);
    }

    /// <summary>The pair the bundle accumulator needs: whether the frame is the delimiter, and whether it is a terminal packet (which inside an open bundle is a protocol error).</summary>
    internal static void ReadBundleIdentity(
        IFrameCodecBinding binding,
        bool resolved,
        in ResolvedFrame frame,
        ProtocolPhase phase,
        PacketFlow flow,
        int wireId,
        ReadOnlySpan<byte> body,
        out bool isDelimiter,
        out bool isTerminal)
    {
        if (resolved)
        {
            isDelimiter = frame.Has(FrameRole.BundleDelimiter);
            isTerminal = frame.Has(FrameRole.Terminal);
            return;
        }

        var context = new FrameDecodeContext(phase, flow, wireId, body);
        isDelimiter = binding.IsBundleDelimiter(in context);
        isTerminal = binding.IsTerminal(in context, out _);
    }

    /// <summary>The three pause-gate identity checks. <paramref name="decoded"/> carries the caller's "this frame produced a packet" condition, because the phase gate is armed only for a decoded frame and the terminal question is not asked when it did not.</summary>
    internal static void ReadGates(
        IFrameCodecBinding binding,
        bool resolved,
        in ResolvedFrame frame,
        ProtocolPhase phase,
        PacketFlow flow,
        int wireId,
        ReadOnlySpan<byte> body,
        bool decoded,
        out bool compressionEnablePoint,
        out bool encryptionEnablePoint,
        out bool terminal)
    {
        if (resolved)
        {
            compressionEnablePoint = frame.Has(FrameRole.CompressionPoint);
            encryptionEnablePoint = frame.Has(FrameRole.EncryptionPoint);

            // The phase gate stays behind "this frame decoded". A frame-mode read loop leaves nothing decoded, and arming the phase gate for a frame nothing decoded parks the reader on a transition the consumer never learns about, which on a fast pipe is a session desync.
            terminal = decoded && frame.Has(FrameRole.Terminal);
            return;
        }

        var context = new FrameDecodeContext(phase, flow, wireId, body);
        compressionEnablePoint = binding.IsCompressionEnablePoint(in context);
        encryptionEnablePoint = binding.IsEncryptionEnablePoint(in context);
        terminal = decoded && binding.IsTerminal(in context, out _);
    }

    /// <summary>The whole sequence a play-phase frame takes on a bundle-aware connection in item mode: resolve and decode, the bundle-identity pair, then the gate checks.</summary>
    internal static FrameDispatchOutcome Run(
        IFrameCodecBinding binding,
        ProtocolPhase phase,
        PacketFlow flow,
        int wireId,
        ReadOnlySpan<byte> body)
    {
        bool known = ResolveAndDecode(
            binding, phase, flow, wireId, body, decode: true,
            out bool resolved, out ResolvedFrame frame, out object packet);
        ReadBundleIdentity(
            binding, resolved, in frame, phase, flow, wireId, body,
            out bool isDelimiter, out bool terminalInBundle);
        ReadGates(
            binding, resolved, in frame, phase, flow, wireId, body, known,
            out bool compressionEnablePoint, out bool encryptionEnablePoint, out bool terminal);

        return new FrameDispatchOutcome(
            known ? packet : null,
            isDelimiter,
            terminalInBundle,
            compressionEnablePoint,
            encryptionEnablePoint,
            terminal);
    }
}

/// <summary>What <see cref="FrameDispatchSequence.Run"/> learned about one inbound frame.</summary>
internal readonly record struct FrameDispatchOutcome(
    object? Packet,
    bool IsBundleDelimiter,
    bool IsTerminalInBundle,
    bool IsCompressionEnablePoint,
    bool IsEncryptionEnablePoint,
    bool IsTerminal);
