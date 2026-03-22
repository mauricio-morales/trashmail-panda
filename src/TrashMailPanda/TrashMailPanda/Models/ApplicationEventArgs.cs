namespace TrashMailPanda.Models;

/// <summary>
/// EventArgs wrapper for the unified application event stream.
/// Subscribers pattern-match on <see cref="Event"/> to handle specific event types.
/// </summary>
public sealed class ApplicationEventArgs : EventArgs
{
    /// <summary>The concrete application event.</summary>
    public ApplicationEvent Event { get; }

    public ApplicationEventArgs(ApplicationEvent @event)
    {
        Event = @event ?? throw new ArgumentNullException(nameof(@event));
    }
}
