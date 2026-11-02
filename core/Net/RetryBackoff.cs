namespace Mawaqit.Core;

/// <summary>When to try the network again after a failed refresh: 30 s, 1, 2, 5, 10, then every 15 min.</summary>
public sealed class RetryBackoff
{
    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
    ];

    public int Failures { get; private set; }
    public DateTimeOffset? NextAttempt { get; private set; }

    public void Failed(DateTimeOffset now)
    {
        NextAttempt = now + Steps[Math.Min(Failures, Steps.Length - 1)];
        Failures++;
    }

    public void Succeeded()
    {
        Failures = 0;
        NextAttempt = null;
    }

    /// <summary>The network came back: allow an attempt now without resetting the step.</summary>
    public void RetrySoon(DateTimeOffset now)
    {
        if (Failures > 0) NextAttempt = now;
    }

    public bool IsDue(DateTimeOffset now) => NextAttempt is { } at && now >= at;
}
