using Umpk.Transport;
using Xunit;

namespace Umpk.Core.Tests.Transport;

public sealed class MessageConnectionContractTests
{
    [Fact]
    public void ClosedTerminationsDoNotCarryACause()
    {
        Assert.Equal(MessageConnectionTerminationReason.LocalGracefulClose, MessageConnectionTermination.LocalGracefulClose.Reason);
        Assert.Equal(MessageConnectionTerminationReason.PeerClosed, MessageConnectionTermination.PeerClosed.Reason);
        Assert.Null(MessageConnectionTermination.LocalGracefulClose.Cause);
        Assert.Null(MessageConnectionTermination.PeerClosed.Cause);
    }

    [Fact]
    public void FailureTerminationRequiresAndPreservesTheCause()
    {
        var error = new InvalidOperationException("transport failed");

        MessageConnectionTermination termination = MessageConnectionTermination.TransportFailure(error);

        Assert.Equal(MessageConnectionTerminationReason.TransportFailure, termination.Reason);
        Assert.Same(error, termination.Cause);
        Assert.Throws<ArgumentNullException>(() => MessageConnectionTermination.ProtocolFailure(null!));
        Assert.Throws<ArgumentNullException>(() => MessageConnectionTermination.TransportFailure(null!));
    }

    [Fact]
    public void AbortedTerminationMayCarryTheAbortCause()
    {
        var error = new OperationCanceledException("aborted");

        MessageConnectionTermination withoutCause = MessageConnectionTermination.Aborted();
        MessageConnectionTermination withCause = MessageConnectionTermination.Aborted(error);

        Assert.Equal(MessageConnectionTerminationReason.Aborted, withoutCause.Reason);
        Assert.Null(withoutCause.Cause);
        Assert.Same(error, withCause.Cause);
    }

    [Fact]
    public void ConnectionContractSeparatesAdmissionFromTerminalCompletion()
    {
        Type contract = typeof(IMessageConnection);

        Assert.Equal(typeof(Task<MessageConnectionTermination>), contract.GetProperty(nameof(IMessageConnection.Completion))!.PropertyType);
        Assert.Equal(typeof(ValueTask<MessageAdmissionResult>), contract.GetMethod(nameof(IMessageConnection.SendAsync))!.ReturnType);
        Assert.Equal(typeof(ValueTask<OwnedMessage?>), contract.GetMethod(nameof(IMessageConnection.ReceiveAsync))!.ReturnType);
        Assert.Equal(new[] { typeof(TimeSpan), typeof(CancellationToken) }, contract.GetMethod(nameof(IMessageConnection.CloseAsync))!.GetParameters().Select(parameter => parameter.ParameterType));
    }
}
