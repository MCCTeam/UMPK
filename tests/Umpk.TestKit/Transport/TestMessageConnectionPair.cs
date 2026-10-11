using Umpk.Transport;

namespace Umpk.TestKit.Transport;

public sealed class TestMessageConnectionPair : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Direction _firstToSecond;
    private readonly Direction _secondToFirst;
    private readonly TimeProvider _timeProvider;
    private readonly Endpoint _first;
    private readonly Endpoint _second;
    private Task? _closeTask;
    private bool _accepting = true;
    private bool _terminal;

    private TestMessageConnectionPair(int capacity, TimeProvider timeProvider)
    {
        _firstToSecond = new Direction(capacity);
        _secondToFirst = new Direction(capacity);
        _timeProvider = timeProvider;
        _first = new Endpoint(this);
        _second = new Endpoint(this);
        First = _first;
        Second = _second;
    }

    public IMessageConnection First { get; }

    public IMessageConnection Second { get; }

    public static TestMessageConnectionPair Create(int capacity, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        return new TestMessageConnectionPair(capacity, timeProvider ?? TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        Abort(_first, MessageConnectionTermination.Aborted());

        Task closeTask;
        lock (_gate)
        {
            closeTask = _closeTask ?? Task.CompletedTask;
        }

        await closeTask.ConfigureAwait(false);
    }

    private ValueTask<MessageAdmissionResult> SendAsync(Endpoint sender, OwnedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_accepting)
            {
                return ValueTask.FromResult(MessageAdmissionResult.Closed);
            }

            Direction direction = ReferenceEquals(sender, _first) ? _firstToSecond : _secondToFirst;
            if (!direction.TryEnqueue(message))
            {
                return ValueTask.FromResult(MessageAdmissionResult.CapacityExceeded);
            }

            return ValueTask.FromResult(MessageAdmissionResult.Accepted);
        }
    }

    private ValueTask<MessageSendReservationResult> ReserveSendAsync(Endpoint sender, int messageLength, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(messageLength);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_accepting)
            {
                return ValueTask.FromResult(MessageSendReservationResult.Closed);
            }

            Direction direction = ReferenceEquals(sender, _first) ? _firstToSecond : _secondToFirst;
            if (!direction.TryReserve())
            {
                return ValueTask.FromResult(MessageSendReservationResult.CapacityExceeded);
            }

            return ValueTask.FromResult(MessageSendReservationResult.Accepted(new SendReservation(this, direction, messageLength)));
        }
    }

    private MessageAdmissionResult CommitReservation(Direction direction, OwnedMessage message)
    {
        lock (_gate)
        {
            if (!_accepting)
            {
                direction.ReleaseReservation();
                return MessageAdmissionResult.Closed;
            }

            direction.CommitReservation(message);
            return MessageAdmissionResult.Accepted;
        }
    }

    private void ReleaseReservation(Direction direction)
    {
        lock (_gate)
        {
            direction.ReleaseReservation();
        }
    }

    private async ValueTask<OwnedMessage?> ReceiveAsync(Endpoint receiver, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref receiver.ReceiveActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("Only one receive may be active per connection.");
        }

        try
        {
            while (true)
            {
                Task changed;
                lock (_gate)
                {
                    Direction direction = ReferenceEquals(receiver, _first) ? _secondToFirst : _firstToSecond;
                    if (direction.TryDequeue(out OwnedMessage? message))
                    {
                        return message;
                    }

                    if (_terminal)
                    {
                        return null;
                    }

                    changed = direction.Changed;
                }

                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref receiver.ReceiveActive, 0);
        }
    }

    private ValueTask CloseAsync(Endpoint closer, TimeSpan drainTimeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(drainTimeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        Task closeTask;
        lock (_gate)
        {
            if (_terminal)
            {
                return ValueTask.CompletedTask;
            }

            _accepting = false;
            if (_closeTask is null)
            {
                Direction outbound = ReferenceEquals(closer, _first) ? _firstToSecond : _secondToFirst;
                Task drain = outbound.Drained;
                if (drain.IsCompleted || drainTimeout == TimeSpan.Zero)
                {
                    Terminate(closer, MessageConnectionTermination.LocalGracefulClose, MessageConnectionTermination.PeerClosed);
                    _closeTask = Task.CompletedTask;
                }
                else
                {
                    _closeTask = MonitorCloseAsync(closer, drain, drainTimeout);
                }
            }

            closeTask = _closeTask;
        }

        return WaitForCloseAsync(closeTask, cancellationToken);
    }

    private async Task MonitorCloseAsync(Endpoint closer, Task drain, TimeSpan drainTimeout)
    {
        try
        {
            await drain.WaitAsync(drainTimeout, _timeProvider, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        lock (_gate)
        {
            Terminate(closer, MessageConnectionTermination.LocalGracefulClose, MessageConnectionTermination.PeerClosed);
        }
    }

    private static async ValueTask WaitForCloseAsync(Task closeTask, CancellationToken cancellationToken)
    {
        await closeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Abort(Endpoint source, MessageConnectionTermination termination)
    {
        ArgumentNullException.ThrowIfNull(termination);
        lock (_gate)
        {
            Terminate(source, termination, termination);
        }
    }

    private void Terminate(Endpoint source, MessageConnectionTermination sourceTermination, MessageConnectionTermination peerTermination)
    {
        if (_terminal)
        {
            return;
        }

        _terminal = true;
        _accepting = false;
        _firstToSecond.DisposeQueuedMessages();
        _secondToFirst.DisposeQueuedMessages();

        Endpoint peer = ReferenceEquals(source, _first) ? _second : _first;
        source.SetCompletion(sourceTermination);
        peer.SetCompletion(peerTermination);
    }

    private sealed class Endpoint(TestMessageConnectionPair pair) : IMessageConnection, IMessageSendReservationProvider
    {
        private readonly TaskCompletionSource<MessageConnectionTermination> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int ReceiveActive;

        public Task<MessageConnectionTermination> Completion => _completion.Task;

        public ValueTask<OwnedMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            return pair.ReceiveAsync(this, cancellationToken);
        }

        public ValueTask<MessageAdmissionResult> SendAsync(OwnedMessage message, CancellationToken cancellationToken = default)
        {
            return pair.SendAsync(this, message, cancellationToken);
        }

        public ValueTask<MessageSendReservationResult> ReserveSendAsync(int messageLength, CancellationToken cancellationToken = default)
        {
            return pair.ReserveSendAsync(this, messageLength, cancellationToken);
        }

        public ValueTask CloseAsync(TimeSpan drainTimeout, CancellationToken cancellationToken = default)
        {
            return pair.CloseAsync(this, drainTimeout, cancellationToken);
        }

        public void Abort(MessageConnectionTermination termination)
        {
            pair.Abort(this, termination);
        }

        public ValueTask DisposeAsync()
        {
            return pair.DisposeAsync();
        }

        internal void SetCompletion(MessageConnectionTermination termination)
        {
            _completion.TrySetResult(termination);
        }
    }

    private sealed class SendReservation(TestMessageConnectionPair pair, Direction direction, int messageLength) : IMessageSendReservation
    {
        private int _state;

        public int MessageLength { get; } = messageLength;

        public ValueTask<MessageAdmissionResult> CommitAsync(OwnedMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                throw new InvalidOperationException("The send reservation is no longer active.");
            }

            if (message.Length != MessageLength)
            {
                pair.ReleaseReservation(direction);
                throw new ArgumentException("The message length does not match the reservation.", nameof(message));
            }

            return ValueTask.FromResult(pair.CommitReservation(direction, message));
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                pair.ReleaseReservation(direction);
            }
        }
    }

    private sealed class Direction(int capacity)
    {
        private readonly Queue<OwnedMessage> _messages = new(capacity);
        private readonly int _capacity = capacity;
        private TaskCompletionSource<bool> _changed = NewSignal();
        private TaskCompletionSource<bool> _drained = CompletedSignal();
        private int _reserved;

        public Task Changed => _changed.Task;

        public Task Drained => _drained.Task;

        public bool TryEnqueue(OwnedMessage message)
        {
            if (_messages.Count + _reserved == _capacity)
            {
                return false;
            }

            if (_messages.Count == 0)
            {
                _drained = NewSignal();
            }

            _messages.Enqueue(message);
            SignalChanged();
            return true;
        }

        public bool TryReserve()
        {
            if (_messages.Count + _reserved == _capacity)
            {
                return false;
            }

            _reserved++;
            return true;
        }

        public void CommitReservation(OwnedMessage message)
        {
            ReleaseReservation();
            if (_messages.Count == 0)
            {
                _drained = NewSignal();
            }

            _messages.Enqueue(message);
            SignalChanged();
        }

        public void ReleaseReservation()
        {
            if (_reserved == 0)
            {
                throw new InvalidOperationException("No send reservation is active.");
            }

            _reserved--;
        }

        public bool TryDequeue(out OwnedMessage? message)
        {
            if (!_messages.TryDequeue(out message))
            {
                return false;
            }

            if (_messages.Count == 0)
            {
                _drained.TrySetResult(true);
            }

            return true;
        }

        public void DisposeQueuedMessages()
        {
            while (_messages.TryDequeue(out OwnedMessage? message))
            {
                message.Dispose();
            }

            _drained.TrySetResult(true);
            SignalChanged();
        }

        private void SignalChanged()
        {
            TaskCompletionSource<bool> changed = _changed;
            _changed = NewSignal();
            changed.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static TaskCompletionSource<bool> CompletedSignal()
        {
            TaskCompletionSource<bool> signal = NewSignal();
            signal.SetResult(true);
            return signal;
        }
    }
}
