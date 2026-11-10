namespace MailStripper.Imap.Services;

public interface IBackoffPolicy
{
    int CurrentAttempt { get; }
    TimeSpan NextDelay();
    void Reset();
}

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
        double delayMs = _initialDelay.TotalMilliseconds * Math.Pow(_multiplier, _attempt);
        _attempt++;

        // Add 0-25% randomized jitter to mitigate thundering herd
        double jitter = delayMs * 0.25 * _random.NextDouble();
        return TimeSpan.FromMilliseconds(Math.Min(delayMs + jitter, _maxDelay.TotalMilliseconds));
    }

    public void Reset()
    {
        _attempt = 0;
    }
}
