namespace MailStripper.Imap.Models;

public class ImapOptions
{
    public const string SectionName = "Imap";

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string MonitoredFolder { get; set; } = "@Safe-Unsubscribe";
    public string ArchiveFolder { get; set; } = "@Safe-Unsubscribe/Archive";
    public string QuarantineFolder { get; set; } = "@Safe-Unsubscribe/Quarantine";
    public string AuditFolder { get; set; } = "@Safe-Unsubscribe/Audit";

    public string MailStripperUrl { get; set; } = "http://localhost:5001";
    public string? TrustedAuthservId { get; set; }

    /// <summary>
    /// When true, qualifying OneClickPostCandidate messages are automatically unsubscribed
    /// using isolated zero-cookie sterile HTTP POST requests without user intervention.
    /// Default is false (dry-run / tag-only mode).
    /// </summary>
    public bool AutoExecuteSterileUnsubscribe { get; set; } = false;

    public int MaxMessageByteSize { get; set; } = 15 * 1024 * 1024; // 15 MB cap

    public int ReconnectInitialDelaySeconds { get; set; } = 2;
    public int ReconnectMaxDelaySeconds { get; set; } = 300;
}

public record ForensicReportDto(
    string Classification,
    string Provenance,
    int RiskScore,
    string SummaryVerdict,
    string ReconWarning,
    bool AuthservIdVerified,
    string? MatchedAuthservId,
    string? PrimaryTargetHost,
    string? SterileCurlCommand,
    List<string> PoisonSignals,
    List<ForensicCandidateDto> Candidates
);

public record ForensicCandidateDto(
    string Source,
    string SanitizedUri,
    string Scheme,
    string Host,
    bool AlignedWithSenderOrEsp,
    List<string> RiskFlags
);

public record StripFileResponse(
    string Id,
    string OneSentenceTitle,
    string ShortSummary,
    ForensicReportDto? Unsubscribe
);
