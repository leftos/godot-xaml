namespace GodotXaml.LanguageServer;

/// <summary>Waits out a debounce; the tests release each wait by hand instead of by the clock.</summary>
internal interface IDebounceScheduler
{
    /// <summary>A task that completes once the delay has passed, or is cancelled with the token.</summary>
    /// <param name="delay">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The wait.</returns>
    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>The clock's debounce: <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
internal sealed class TaskDelayScheduler : IDebounceScheduler
{
    /// <inheritdoc/>
    public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
