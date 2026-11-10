using MailStripper.Imap.Models;

namespace MailStripper.Imap.Services;

public class ImapHealthTracker : IImapHealthTracker
{
    private readonly object _lock = new();
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
    private bool _isConnected;
    private DateTimeOffset? _lastConnectedUtc;
    private DateTimeOffset? _lastDisconnectedUtc;
    private DateTimeOffset? _lastMessageProcessedUtc;
    private long _messagesProcessedCount;
    private string? _lastError;

    public bool IsConnected
    {
        get
        {
            lock (_lock) return _isConnected;
        }
    }

    public bool IsHealthy => IsConnected;

    public string Status
    {
        get
        {
            lock (_lock) return ComputeStatus();
        }
    }

    private string ComputeStatus() =>
        _isConnected ? "Healthy"
        : (_lastConnectedUtc == null && _lastDisconnectedUtc == null ? "Starting" : "Unhealthy");

    public DateTimeOffset StartedUtc => _startedUtc;

    public DateTimeOffset? LastConnectedUtc
    {
        get
        {
            lock (_lock) return _lastConnectedUtc;
        }
    }

    public DateTimeOffset? LastDisconnectedUtc
    {
        get
        {
            lock (_lock) return _lastDisconnectedUtc;
        }
    }

    public DateTimeOffset? LastMessageProcessedUtc
    {
        get
        {
            lock (_lock) return _lastMessageProcessedUtc;
        }
    }

    public long MessagesProcessedCount => Interlocked.Read(ref _messagesProcessedCount);

    public string? LastError
    {
        get
        {
            lock (_lock) return _lastError;
        }
    }

    public void ReportConnected()
    {
        lock (_lock)
        {
            _isConnected = true;
            _lastConnectedUtc = DateTimeOffset.UtcNow;
            _lastError = null;
        }
    }

    public void ReportDisconnected(string? reason)
    {
        lock (_lock)
        {
            _isConnected = false;
            _lastDisconnectedUtc = DateTimeOffset.UtcNow;
            _lastError = reason;
        }
    }

    public void ReportMessageProcessed()
    {
        lock (_lock)
        {
            _lastMessageProcessedUtc = DateTimeOffset.UtcNow;
        }
        Interlocked.Increment(ref _messagesProcessedCount);
    }

    public HealthReportDto GetReport()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            return new HealthReportDto(
                Status: ComputeStatus(),
                Connected: _isConnected,
                Healthy: _isConnected,
                StartedUtc: _startedUtc,
                UptimeSeconds: Math.Round((now - _startedUtc).TotalSeconds, 2),
                LastConnectedUtc: _lastConnectedUtc,
                LastDisconnectedUtc: _lastDisconnectedUtc,
                LastMessageProcessedUtc: _lastMessageProcessedUtc,
                MessagesProcessedCount: Interlocked.Read(ref _messagesProcessedCount),
                LastError: _lastError
            );
        }
    }
}
