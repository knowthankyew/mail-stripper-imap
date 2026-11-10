namespace MailStripper.Imap.Models;

public record HealthReportDto(
    string Status,
    bool Connected,
    bool Healthy,
    DateTimeOffset StartedUtc,
    double UptimeSeconds,
    DateTimeOffset? LastConnectedUtc,
    DateTimeOffset? LastDisconnectedUtc,
    DateTimeOffset? LastMessageProcessedUtc,
    long MessagesProcessedCount,
    string? LastError
);
