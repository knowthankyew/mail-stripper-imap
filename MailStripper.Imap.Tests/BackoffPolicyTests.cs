using System;
using MailStripper.Imap.Services;
using Xunit;

namespace MailStripper.Imap.Tests;

public class BackoffPolicyTests
{
    [Fact]
    public void NextDelay_ScalesExponentially()
    {
        // Random returning 0 jitter to verify base exponential scaling
        var fakeRandom = new DeterministicRandom(0.0);
        var policy = new ExponentialBackoffPolicy(
            initialDelay: TimeSpan.FromSeconds(2),
            maxDelay: TimeSpan.FromSeconds(60),
            multiplier: 2.0,
            random: fakeRandom);

        // Attempt 0: 2s * 2^0 = 2s
        var delay0 = policy.NextDelay();
        Assert.Equal(TimeSpan.FromSeconds(2), delay0);
        Assert.Equal(1, policy.CurrentAttempt);

        // Attempt 1: 2s * 2^1 = 4s
        var delay1 = policy.NextDelay();
        Assert.Equal(TimeSpan.FromSeconds(4), delay1);
        Assert.Equal(2, policy.CurrentAttempt);

        // Attempt 2: 2s * 2^2 = 8s
        var delay2 = policy.NextDelay();
        Assert.Equal(TimeSpan.FromSeconds(8), delay2);
        Assert.Equal(3, policy.CurrentAttempt);

        // Attempt 3: 2s * 2^3 = 16s
        var delay3 = policy.NextDelay();
        Assert.Equal(TimeSpan.FromSeconds(16), delay3);
        Assert.Equal(4, policy.CurrentAttempt);
    }

    [Fact]
    public void NextDelay_CapsAtMaxDelay()
    {
        var fakeRandom = new DeterministicRandom(0.0);
        var policy = new ExponentialBackoffPolicy(
            initialDelay: TimeSpan.FromSeconds(10),
            maxDelay: TimeSpan.FromSeconds(30),
            multiplier: 2.0,
            random: fakeRandom);

        policy.NextDelay(); // 10s
        policy.NextDelay(); // 20s
        var delay2 = policy.NextDelay(); // 40s -> capped at 30s
        var delay3 = policy.NextDelay(); // 80s -> capped at 30s

        Assert.Equal(TimeSpan.FromSeconds(30), delay2);
        Assert.Equal(TimeSpan.FromSeconds(30), delay3);
    }

    [Fact]
    public void NextDelay_AppliesJitterWithinBounds()
    {
        // Test with max jitter factor (1.0 => 25% extra)
        var maxJitterRandom = new DeterministicRandom(1.0);
        var policy = new ExponentialBackoffPolicy(
            initialDelay: TimeSpan.FromSeconds(10),
            maxDelay: TimeSpan.FromSeconds(60),
            multiplier: 2.0,
            random: maxJitterRandom);

        var delay = policy.NextDelay(); // 10s base + 2.5s jitter = 12.5s
        Assert.Equal(TimeSpan.FromMilliseconds(12500), delay);
    }

    [Fact]
    public void Reset_ClearsCurrentAttempt()
    {
        var policy = new ExponentialBackoffPolicy(TimeSpan.FromSeconds(1));
        policy.NextDelay();
        policy.NextDelay();
        Assert.Equal(2, policy.CurrentAttempt);

        policy.Reset();
        Assert.Equal(0, policy.CurrentAttempt);

        var firstDelay = policy.NextDelay();
        Assert.True(firstDelay.TotalSeconds >= 1.0 && firstDelay.TotalSeconds <= 1.25);
    }

    private class DeterministicRandom : Random
    {
        private readonly double _value;

        public DeterministicRandom(double value)
        {
            _value = value;
        }

        public override double NextDouble() => _value;
    }
}
