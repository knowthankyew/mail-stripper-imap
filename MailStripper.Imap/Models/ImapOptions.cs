using System.ComponentModel.DataAnnotations;

namespace MailStripper.Imap.Models;

public class ImapOptions : IValidatableObject
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

    [Range(1, 3600, ErrorMessage = "ReconnectInitialDelaySeconds must be between 1 and 3600 seconds.")]
    public int ReconnectInitialDelaySeconds { get; set; } = 2;

    [Range(1, 86400, ErrorMessage = "ReconnectMaxDelaySeconds must be between 1 and 86400 seconds.")]
    public int ReconnectMaxDelaySeconds { get; set; } = 300;

    public bool HealthCheckEnabled { get; set; } = true;

    [Range(1, 65535, ErrorMessage = "HealthCheckPort must be between 1 and 65535.")]
    public int HealthCheckPort { get; set; } = 8080;

    public string HealthCheckHost { get; set; } = "127.0.0.1";

    // Configuration aliases supporting snake_case environment variables
    // (e.g. IMAP__USE_SSL, IMAP__MONITORED_FOLDER, IMAP__MAIL_STRIPPER_URL, etc.)
    public bool? Use_Ssl { get => UseSsl; set { if (value.HasValue) UseSsl = value.Value; } }
    public string? Monitored_Folder { get => MonitoredFolder; set { if (value != null) MonitoredFolder = value; } }
    public string? Archive_Folder { get => ArchiveFolder; set { if (value != null) ArchiveFolder = value; } }
    public string? Quarantine_Folder { get => QuarantineFolder; set { if (value != null) QuarantineFolder = value; } }
    public string? Audit_Folder { get => AuditFolder; set { if (value != null) AuditFolder = value; } }
    public string? Mail_Stripper_Url { get => MailStripperUrl; set { if (value != null) MailStripperUrl = value; } }
    public string? Trusted_Authserv_Id { get => TrustedAuthservId; set => TrustedAuthservId = value; }
    public bool? Auto_Execute_Sterile_Unsubscribe { get => AutoExecuteSterileUnsubscribe; set { if (value.HasValue) AutoExecuteSterileUnsubscribe = value.Value; } }
    public int? Max_Message_Byte_Size { get => MaxMessageByteSize; set { if (value.HasValue) MaxMessageByteSize = value.Value; } }
    public int? Reconnect_Initial_Delay_Seconds { get => ReconnectInitialDelaySeconds; set { if (value.HasValue) ReconnectInitialDelaySeconds = value.Value; } }
    public int? Reconnect_Max_Delay_Seconds { get => ReconnectMaxDelaySeconds; set { if (value.HasValue) ReconnectMaxDelaySeconds = value.Value; } }
    public bool? Health_Check_Enabled { get => HealthCheckEnabled; set { if (value.HasValue) HealthCheckEnabled = value.Value; } }
    public int? Health_Check_Port { get => HealthCheckPort; set { if (value.HasValue) HealthCheckPort = value.Value; } }
    public string? Health_Check_Host { get => HealthCheckHost; set { if (value != null) HealthCheckHost = value; } }

    /// <summary>
    /// Fallback resolution for flat single-underscore environment variables (e.g. IMAP_HOST, IMAP_PORT).
    /// Standard .NET configuration hierarchy (IMAP__HOST, IMAP__USE_SSL) takes precedence.
    /// </summary>
    public void ApplyEnvironmentFallbacks(Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;

        if (getEnv("IMAP_HOST") is { Length: > 0 } host) Host = host;
        if (getEnv("IMAP_PORT") is { Length: > 0 } portStr && int.TryParse(portStr, out var port)) Port = port;
        if (getEnv("IMAP_USE_SSL") is { Length: > 0 } sslStr && bool.TryParse(sslStr, out var ssl)) UseSsl = ssl;
        if (getEnv("IMAP_USERNAME") is { Length: > 0 } username) Username = username;
        if (getEnv("IMAP_PASSWORD") is { Length: > 0 } password) Password = password;
        if (getEnv("IMAP_MONITORED_FOLDER") is { Length: > 0 } monitored) MonitoredFolder = monitored;
        if (getEnv("IMAP_ARCHIVE_FOLDER") is { Length: > 0 } archive) ArchiveFolder = archive;
        if (getEnv("IMAP_QUARANTINE_FOLDER") is { Length: > 0 } quarantine) QuarantineFolder = quarantine;
        if (getEnv("IMAP_AUDIT_FOLDER") is { Length: > 0 } audit) AuditFolder = audit;
        if (getEnv("IMAP_MAIL_STRIPPER_URL") is { Length: > 0 } url) MailStripperUrl = url;
        if (getEnv("IMAP_TRUSTED_AUTHSERV_ID") is { Length: > 0 } authserv) TrustedAuthservId = authserv;
        if (getEnv("IMAP_AUTO_EXECUTE_STERILE_UNSUBSCRIBE") is { Length: > 0 } autoStr && bool.TryParse(autoStr, out var autoExec)) AutoExecuteSterileUnsubscribe = autoExec;
        if (getEnv("IMAP_HEALTH_CHECK_ENABLED") is { Length: > 0 } healthEnabledStr && bool.TryParse(healthEnabledStr, out var healthEnabled)) HealthCheckEnabled = healthEnabled;
        if (getEnv("IMAP_HEALTH_CHECK_PORT") is { Length: > 0 } healthPortStr && int.TryParse(healthPortStr, out var healthPort)) HealthCheckPort = healthPort;
        else if (getEnv("HEALTH_PORT") is { Length: > 0 } hPortStr && int.TryParse(hPortStr, out var hPort)) HealthCheckPort = hPort;
        if (getEnv("IMAP_HEALTH_CHECK_HOST") is { Length: > 0 } healthHost) HealthCheckHost = healthHost;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ReconnectMaxDelaySeconds < ReconnectInitialDelaySeconds)
        {
            yield return new ValidationResult(
                "ReconnectMaxDelaySeconds must be greater than or equal to ReconnectInitialDelaySeconds.",
                new[] { nameof(ReconnectMaxDelaySeconds) });
        }
    }
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
