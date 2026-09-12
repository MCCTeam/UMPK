namespace Umpk.Client.Events;

/// <summary>How a bounded event stream behaves when its buffer is full.</summary>
public enum StreamFullPolicy
{
    /// <summary>Drop the oldest buffered item and raise a <see cref="StreamLagged"/> diagnostic.</summary>
    DropOldest,

    /// <summary>Fault the stream enumeration with an exception.</summary>
    Throw,
}
