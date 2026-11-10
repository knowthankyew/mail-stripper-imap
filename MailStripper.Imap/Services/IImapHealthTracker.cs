using MailStripper.Imap.Models;

namespace MailStripper.Imap.Services;

public interface IImapHealthTracker
{
    bool IsConnected { get; }
    bool IsHealthy { get; }
    string Status { get; }
    DateTimeOffset StartedUtc { get; }
    DateTimeOffset? LastConnectedUtc { get; }
    DateTimeOffset? LastDisconnectedUtc { get; }
    DateTimeOffset? LastMessageProcessedUtc { get; }
    long MessagesProcessedCount { get; }
    string? LastError { get; }

    void ReportConnected();
    void ReportDisconnected(string? reason);
    void ReportMessageProcessed();
    HealthReportDto GetReport();
}
