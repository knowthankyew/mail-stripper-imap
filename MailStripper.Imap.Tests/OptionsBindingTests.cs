using System.ComponentModel.DataAnnotations;
using MailStripper.Imap.Models;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailStripper.Imap.Tests;

public class OptionsBindingTests
{
    [Fact]
    public void ConfigurationBinding_SnakeCaseAliases_PopulateOptionsCorrectly()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Imap:Host"] = "imap.custom.com",
            ["Imap:Port"] = "9933",
            ["Imap:Use_Ssl"] = "false",
            ["Imap:Monitored_Folder"] = "INBOX/Testing",
            ["Imap:Archive_Folder"] = "INBOX/Archive",
            ["Imap:Quarantine_Folder"] = "INBOX/Quarantine",
            ["Imap:Audit_Folder"] = "INBOX/Audit",
            ["Imap:Mail_Stripper_Url"] = "http://stripper-service:5001",
            ["Imap:Trusted_Authserv_Id"] = "auth.example.com",
            ["Imap:Auto_Execute_Sterile_Unsubscribe"] = "true",
            ["Imap:Max_Message_Byte_Size"] = "20971520",
            ["Imap:Reconnect_Initial_Delay_Seconds"] = "5",
            ["Imap:Reconnect_Max_Delay_Seconds"] = "600",
            ["Imap:Health_Check_Enabled"] = "true",
            ["Imap:Health_Check_Port"] = "9090",
            ["Imap:Health_Check_Host"] = "0.0.0.0"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemory)
            .Build();

        var options = new ImapOptions();
        config.GetSection("Imap").Bind(options);

        Assert.Equal("imap.custom.com", options.Host);
        Assert.Equal(9933, options.Port);
        Assert.False(options.UseSsl);
        Assert.Equal("INBOX/Testing", options.MonitoredFolder);
        Assert.Equal("INBOX/Archive", options.ArchiveFolder);
        Assert.Equal("INBOX/Quarantine", options.QuarantineFolder);
        Assert.Equal("INBOX/Audit", options.AuditFolder);
        Assert.Equal("http://stripper-service:5001", options.MailStripperUrl);
        Assert.Equal("auth.example.com", options.TrustedAuthservId);
        Assert.True(options.AutoExecuteSterileUnsubscribe);
        Assert.Equal(20971520, options.MaxMessageByteSize);
        Assert.Equal(5, options.ReconnectInitialDelaySeconds);
        Assert.Equal(600, options.ReconnectMaxDelaySeconds);
        Assert.True(options.HealthCheckEnabled);
        Assert.Equal(9090, options.HealthCheckPort);
        Assert.Equal("0.0.0.0", options.HealthCheckHost);
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_BindsFlatEnvironmentVariables()
    {
        var mockEnv = new Dictionary<string, string>
        {
            ["IMAP_HOST"] = "mail.provider.com",
            ["IMAP_PORT"] = "1993",
            ["IMAP_USE_SSL"] = "true",
            ["IMAP_USERNAME"] = "user@domain.com",
            ["IMAP_PASSWORD"] = "secret123",
            ["IMAP_MONITORED_FOLDER"] = "@Unsub",
            ["IMAP_ARCHIVE_FOLDER"] = "@Unsub/Done",
            ["IMAP_QUARANTINE_FOLDER"] = "@Unsub/Spam",
            ["IMAP_AUDIT_FOLDER"] = "@Unsub/Logs",
            ["IMAP_MAIL_STRIPPER_URL"] = "http://backend:5001",
            ["IMAP_TRUSTED_AUTHSERV_ID"] = "mx.google.com",
            ["IMAP_AUTO_EXECUTE_STERILE_UNSUBSCRIBE"] = "true",
            ["IMAP_HEALTH_CHECK_PORT"] = "8888",
            ["IMAP_HEALTH_CHECK_HOST"] = "localhost"
        };

        var options = new ImapOptions();
        options.ApplyEnvironmentFallbacks(k => mockEnv.TryGetValue(k, out var val) ? val : null);

        Assert.Equal("mail.provider.com", options.Host);
        Assert.Equal(1993, options.Port);
        Assert.True(options.UseSsl);
        Assert.Equal("user@domain.com", options.Username);
        Assert.Equal("secret123", options.Password);
        Assert.Equal("@Unsub", options.MonitoredFolder);
        Assert.Equal("@Unsub/Done", options.ArchiveFolder);
        Assert.Equal("@Unsub/Spam", options.QuarantineFolder);
        Assert.Equal("@Unsub/Logs", options.AuditFolder);
        Assert.Equal("http://backend:5001", options.MailStripperUrl);
        Assert.Equal("mx.google.com", options.TrustedAuthservId);
        Assert.True(options.AutoExecuteSterileUnsubscribe);
        Assert.Equal(8888, options.HealthCheckPort);
        Assert.Equal("localhost", options.HealthCheckHost);
    }

    [Fact]
    public void ConfigurationBinding_HierarchicalWinsOverFlatFallback()
    {
        var mockEnv = new Dictionary<string, string>
        {
            ["IMAP_HOST"] = "fallback-flat.example.com",
            ["IMAP_PORT"] = "1143"
        };

        var inMemory = new Dictionary<string, string?>
        {
            ["Imap:Host"] = "hierarchical.example.com"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemory)
            .Build();

        var options = new ImapOptions();
        // Fallbacks applied first (via Configure)
        options.ApplyEnvironmentFallbacks(k => mockEnv.TryGetValue(k, out var val) ? val : null);
        // Then configuration section bound second (overriding fallback)
        config.GetSection("Imap").Bind(options);

        // Hierarchical config value in Imap:Host takes precedence over IMAP_HOST
        Assert.Equal("hierarchical.example.com", options.Host);
        // While unconfigured keys fall back to the flat env variable
        Assert.Equal(1143, options.Port);
    }

    [Fact]
    public void Validation_RejectsInvalidPortAndDelayRanges()
    {
        var invalidPortOptions = new ImapOptions
        {
            HealthCheckPort = 70000
        };

        var portResults = new List<ValidationResult>();
        var isPortValid = Validator.TryValidateObject(invalidPortOptions, new ValidationContext(invalidPortOptions), portResults, validateAllProperties: true);

        Assert.False(isPortValid);
        Assert.Contains(portResults, r => r.MemberNames.Contains(nameof(ImapOptions.HealthCheckPort)));

        var invalidDelayOptions = new ImapOptions
        {
            ReconnectInitialDelaySeconds = 10,
            ReconnectMaxDelaySeconds = 2
        };

        var delayResults = new List<ValidationResult>();
        var isDelayValid = Validator.TryValidateObject(invalidDelayOptions, new ValidationContext(invalidDelayOptions), delayResults, validateAllProperties: true);

        Assert.False(isDelayValid);
        Assert.Contains(delayResults, r => r.MemberNames.Contains(nameof(ImapOptions.ReconnectMaxDelaySeconds)));
    }
}
