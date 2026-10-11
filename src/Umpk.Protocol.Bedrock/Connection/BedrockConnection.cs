using System.Buffers;
using Umpk.Transport;

namespace Umpk.Protocol.Bedrock;

internal sealed class BedrockConnection : IAsyncDisposable
{
    private readonly IMessageConnection _transport;
    private readonly IMessageSendReservationProvider _reservationProvider;
    private readonly SemaphoreSlim _egress = new(1, 1);
    private readonly SemaphoreSlim _ingress = new(1, 1);
    private readonly BedrockCodecLimits _limits;
    private BedrockCompressionSettings? _compression;
    private BedrockEncryptionState? _sendEncryption;
    private BedrockEncryptionState? _receiveEncryption;
    private BedrockSessionCodecContext _context;
    private bool _terminal;
    private bool _disposed;

    internal BedrockConnection(IMessageConnection transport, BedrockSessionCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(context);
        _transport = transport;
        _reservationProvider = transport as IMessageSendReservationProvider
            ?? throw new ArgumentException("The transport must support outbound reservations.", nameof(transport));
        _context = context;
        _limits = context.Limits;
    }

    internal BedrockSessionCodecContext Context => _context;

    internal bool IsTerminal => _terminal;

    internal async ValueTask EnableCompressionAsync(BedrockCompressionSettings settings, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        await _egress.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _ingress.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfUnavailable();
                if (_compression.HasValue)
                    throw new InvalidOperationException("Compression is already enabled.");

                _compression = settings;
            }
            finally
            {
                _ingress.Release();
            }
        }
        finally
        {
            _egress.Release();
        }
    }

    internal async ValueTask EnableEncryptionAsync(
        BedrockEncryptionState send,
        BedrockEncryptionState receive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(receive);
        ThrowIfUnavailable();
        await _egress.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _ingress.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfUnavailable();
                if (_sendEncryption is not null || _receiveEncryption is not null)
                    throw new InvalidOperationException("Encryption is already enabled.");

                _sendEncryption = send;
                _receiveEncryption = receive;
            }
            finally
            {
                _ingress.Release();
            }
        }
        finally
        {
            _egress.Release();
        }
    }

    internal async ValueTask<MessageAdmissionResult> SendBatchAsync(
        IReadOnlyList<BedrockBatchMember> members,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(members);
        ThrowIfUnavailable();
        await _egress.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            byte[] batch = BedrockBatchCodec.EncodeMembers(members, _limits);
            byte[] transformed = _compression.HasValue
                ? BedrockCompressionCodec.Encode(batch, _compression.Value, _limits)
                : batch;
            int encryptedOverhead = _sendEncryption is null ? 0 : 8;
            int envelopeLength = checked(1 + transformed.Length + encryptedOverhead);
            MessageSendReservationResult reservationResult = await _reservationProvider
                .ReserveSendAsync(envelopeLength, cancellationToken)
                .ConfigureAwait(false);
            if (!reservationResult.IsReserved)
                return reservationResult.Status switch
                {
                    MessageAdmissionStatus.Closed => MessageAdmissionResult.Closed,
                    MessageAdmissionStatus.CapacityExceeded => MessageAdmissionResult.CapacityExceeded,
                    _ => throw new BedrockCodecException("The transport returned an invalid reservation result."),
                };

            using IMessageSendReservation reservation = reservationResult.Reservation!;
            if (reservation.MessageLength != envelopeLength)
                throw Terminate(new BedrockCodecException("The transport reservation does not match the requested envelope length."));

            try
            {
                if (_sendEncryption is not null)
                    transformed = _sendEncryption.EncryptBatch(transformed);

                byte[] envelope = BedrockEnvelopeCodec.Wrap(transformed);
                IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(envelope.Length);
                envelope.CopyTo(owner.Memory.Span);
                var message = new OwnedMessage(owner, envelope.Length);
                MessageAdmissionResult admission;
                try
                {
                    admission = await reservation.CommitAsync(message).ConfigureAwait(false);
                }
                catch
                {
                    message.Dispose();
                    throw;
                }

                if (!admission.OwnershipTransferred)
                {
                    message.Dispose();
                    throw Terminate(new BedrockCodecException($"The reserved Bedrock envelope was rejected with {admission.Status}."));
                }

                return admission;
            }
            catch (Exception exception) when (exception is not BedrockCodecException || !_terminal)
            {
                throw Terminate(exception);
            }
        }
        finally
        {
            _egress.Release();
        }
    }

    internal async ValueTask<bool> ReceiveAndDispatchAsync(
        IBedrockBatchConsumer consumer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ThrowIfUnavailable();
        await _ingress.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            OwnedMessage? message = await _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
                return false;

            using (message)
            {
                try
                {
                    ReadOnlySpan<byte> envelope = message.Memory.Span;
                    byte[] transformed = BedrockEnvelopeCodec.Unwrap(envelope).ToArray();
                    if (_receiveEncryption is not null)
                        transformed = _receiveEncryption.DecryptBatch(transformed);

                    byte[] batch = _compression.HasValue
                        ? BedrockCompressionCodec.Decode(transformed, _compression.Value, _limits)
                        : transformed;
                    IReadOnlyList<BedrockBatchMember> members = BedrockBatchCodec.DecodeMembers(batch, _limits);
                    foreach (BedrockBatchMember member in members)
                    {
                        BedrockRegistrySnapshot? replacement = consumer.Consume(member, _context);
                        if (replacement is not null)
                            _context = _context with { Registries = replacement };
                    }

                    return true;
                }
                catch (Exception exception)
                {
                    throw Terminate(exception);
                }
            }
        }
        finally
        {
            _ingress.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _terminal = true;
        _sendEncryption?.Dispose();
        _receiveEncryption?.Dispose();
        _egress.Dispose();
        _ingress.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    private BedrockCodecException Terminate(Exception exception)
    {
        _terminal = true;
        var failure = exception as BedrockCodecException
            ?? new BedrockCodecException("The Bedrock protocol stream failed.", exception);
        _transport.Abort(MessageConnectionTermination.ProtocolFailure(failure));
        return failure;
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_terminal)
            throw new BedrockCodecException("The Bedrock protocol stream is terminal.");
    }
}
