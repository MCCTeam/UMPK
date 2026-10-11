using System.Collections.Immutable;

namespace Umpk.Protocol.Bedrock;

internal sealed record BedrockToleratedPacket(int PacketId, int PayloadBytes);

internal sealed class BedrockPacketDispatcher(Action<object, BedrockSessionCodecContext> dispatch, Action<BedrockBatchMember>? admittedObserver = null) : IBedrockBatchConsumer
{
    private readonly Action<object, BedrockSessionCodecContext> _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    private readonly Action<BedrockBatchMember>? _admittedObserver = admittedObserver;

    public BedrockRegistrySnapshot? Consume(BedrockBatchMember member, BedrockSessionCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(context);
        if (Bedrock2169CodecRegistry.TryGet(member.Header.PacketId, out Bedrock2169CodecBinding binding))
        {
            object packet;
            try
            {
                packet = Bedrock2169CodecRegistry.Decode(member, context, out _);
            }
            catch (BedrockCodecException exception)
            {
                throw new BedrockCodecException(
                    $"Packet {member.Header.PacketId} ({binding.Name}) with {member.Payload.Length} payload bytes failed to decode: {exception.Message} (prefix {Convert.ToHexString(member.Payload.AsSpan(0, Math.Min(member.Payload.Length, 32)))}).",
                    exception);
            }
            _dispatch(packet, context);
            _admittedObserver?.Invoke(member);
            if (binding.RegistryGeneration == BedrockRegistryGeneration.None)
                return null;
            if (binding.RegistryGeneration == BedrockRegistryGeneration.ItemRegistry && packet is BedrockItemRegistryPacket itemRegistry)
                return BedrockItemRegistryCodec.Publish(itemRegistry, context.Registries);
            if (binding.RegistryGeneration == BedrockRegistryGeneration.EntityIdentifiers && packet is AvailableEntityIdentifiersPacket entityIdentifiers)
                return PublishEntityIdentifiers(entityIdentifiers, context.Registries);
            throw new BedrockCodecException($"Packet {binding.PacketId} did not produce its declared registry generation.");
        }

        if (Bedrock2169JoinTolerance.IsTolerated(member.Header.PacketId))
        {
            if (context.Routing.Direction != BedrockPacketDirection.Clientbound)
                throw new BedrockCodecException($"Packet {member.Header.PacketId} is clientbound but was dispatched as {context.Routing.Direction}.");
            if (member.Payload.Length > context.Limits.MaxByteArrayBytes)
                throw new BedrockCodecException($"The tolerated packet {member.Header.PacketId} exceeds its byte limit.");
            _dispatch(new BedrockToleratedPacket(member.Header.PacketId, member.Payload.Length), context);
            _admittedObserver?.Invoke(member);
            return null;
        }

        throw new BedrockCodecException($"Packet {member.Header.PacketId} ({Bedrock2169JoinTolerance.Name(member.Header.PacketId)}) has no bound 2169 codec.");
    }

    private static BedrockRegistrySnapshot PublishEntityIdentifiers(AvailableEntityIdentifiersPacket packet, BedrockRegistrySnapshot current)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(current);
        IReadOnlyList<string> identifiers = BedrockJoinCodecs.ReadEntityIdentifiers(packet);
        ImmutableDictionary<string, int>.Builder entities = current.Entities.ToBuilder();
        foreach (string identifier in identifiers)
            entities[identifier] = entities.Count;

        return current with { Generation = checked(current.Generation + 1), Entities = entities.ToImmutable() };
    }
}
