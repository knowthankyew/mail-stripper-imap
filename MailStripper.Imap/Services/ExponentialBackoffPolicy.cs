namespace MailStripper.Imap.Services;

public interface IBackoffPolicy
{
    int CurrentAttempt { get; }
    TimeSpan NextDelay();
    void Reset();
}

/// <summary>
/// Thread-safety contract: This policy is designed for single-threaded sequential retry loops
/// within a single background service instance.
/// </summary>
public class ExponentialBackoffPolicy : IBackoffPolicy
{
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _multiplier;
    private readonly Random _random;
    private int _attempt;

    public ExponentialBackoffPolicy(
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double multiplier = 2.0,
        Random? random = null)
    {
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(2);
        _maxDelay = maxDelay ?? TimeSpan.FromMinutes(5);
        _multiplier = multiplier;
        _random = random ?? Random.Shared;
        _attempt = 0;
    }

    public int CurrentAttempt => _attempt;

    public TimeSpan NextDelay()
    {
        // Cap exponent at 30 to prevent floating-point infinity and NaN during extended retries
        int effectiveExponent = Math.Min(_attempt, 30);
        double delayMs = _initialDelay.TotalMilliseconds * Math.Pow(_multiplier, effectiveExponent);
        delayMs = Math.Min(delayMs, _maxDelay.TotalMilliseconds);
        _attempt++;

        // Add 0-25% randomized jitter to mitigate thundering herd, clamped to max delay
        double jitter = delayMs * 0.25 * _random.NextDouble();
        return TimeSpan.FromMilliseconds(Math.Min(delayMs + jitter, _maxDelay.TotalMilliseconds));
    }

    public void Reset()
    {
        _attempt = 0;
    }
}
