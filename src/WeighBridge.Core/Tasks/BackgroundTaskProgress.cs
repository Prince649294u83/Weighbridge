namespace WeighBridge.Core.Tasks;

/// <summary>
/// Progress reported by a running background task.
/// </summary>
/// <remarks>
/// A readonly record struct because a long task reports progress hundreds of times and
/// each report would otherwise be a heap allocation the collector later has to sweep.
/// </remarks>
/// <param name="Message">What the task is doing now.</param>
/// <param name="PercentComplete">
/// Completion from 0 to 100, or <c>null</c> when the task cannot know — which is honest
/// and drives an indeterminate bar rather than a fabricated one.
/// </param>
public readonly record struct BackgroundTaskProgress(string Message, double? PercentComplete = null)
{
    /// <summary>True when the task cannot estimate how far along it is.</summary>
    public bool IsIndeterminate => PercentComplete is null;
}
