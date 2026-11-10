using System.Security.Cryptography;
using System.Text;
using MailStripper.Imap.Models;
using MailStripper.Imap.Services;
using MimeKit;
using Xunit;

namespace MailStripper.Imap.Tests;

public class MimeAuditGeneratorTests
{
    private static MimeMessage CreateDummyMessage(string subject = "Test Newsletter", string body = "Original Newsletter Body")
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Publisher", "newsletter@example.com"));
        msg.To.Add(new MailboxAddress("Subscriber", "subscriber@domain.local"));
        msg.Subject = subject;
        msg.MessageId = "<msg-12345@example.com>";
        msg.Date = DateTimeOffset.UtcNow.AddMinutes(-5);
        msg.Body = new TextPart("plain") { Text = body };
        return msg;
    }

    private static (MimeMessage Message, byte[] RawBytes) CreateDummyMessageWithBytes()
    {
        var msg = CreateDummyMessage();
        using var ms = new MemoryStream();
        msg.WriteTo(ms);
        return (msg, ms.ToArray());
    }

    [Fact]
    public void GenerateAuditMessage_ProducesValidRfc822WithExpectedForensicHeaders()
    {
        var generator = new MimeAuditGenerator();
        var (original, rawBytes) = CreateDummyMessageWithBytes();
        var expectedSha = Convert.ToHexStringLower(SHA256.HashData(rawBytes));

        var report = new ForensicReportDto(
            Classification: "OneClickPostCandidate",
            Provenance: "TrustedReceivingMx",
            RiskScore: 12,
            SummaryVerdict: "Valid sterile unsubscribe candidate.",
            ReconWarning: string.Empty,
            AuthservIdVerified: true,
            MatchedAuthservId: "mx.google.com",
            PrimaryTargetHost: "example.com",
            SterileCurlCommand: "curl -X POST https://example.com/unsub",
            PoisonSignals: new List<string>(),
            Candidates: new List<ForensicCandidateDto>
            {
                new("HeaderListUnsubscribe", "https://example.com/unsub", "https", "example.com", true, new List<string>())
            }
        );

        var dispatch = new DispatchResult(
            Success: true,
            StatusCode: 200,
            TargetUri: "https://example.com/unsub",
            ExecutedAt: DateTimeOffset.UtcNow,
            ErrorMessage: null
        );

        var context = new AuditContext(original, rawBytes, report, dispatch);
        var audit = generator.GenerateAuditMessage(context);

        // Header assertions
        Assert.NotNull(audit);
        Assert.Equal("audit@mailstripper.local", audit.From.Mailboxes.First().Address);
        Assert.Contains("[AUDIT] OneClickPostCandidate (12/100): Test Newsletter", audit.Subject);
        Assert.Equal("0.3.0", audit.Headers["X-MailStripper-Audit-Version"]);
        Assert.Equal("OneClickPostCandidate", audit.Headers["X-MailStripper-Classification"]);
        Assert.Equal("12", audit.Headers["X-MailStripper-Risk-Score"]);
        Assert.Equal("TrustedReceivingMx", audit.Headers["X-MailStripper-Provenance"]);
        Assert.Equal("msg-12345@example.com", audit.Headers["X-MailStripper-Original-Message-Id"]);
        Assert.Equal(expectedSha, audit.Headers["X-MailStripper-Original-Sha256"]);
        Assert.Equal("yes", audit.Headers["X-MailStripper-Authserv-Verified"]);
        Assert.Equal("mx.google.com", audit.Headers["X-MailStripper-Matched-Authserv"]);
        Assert.Equal("200", audit.Headers["X-MailStripper-Dispatch-Status"]);
        Assert.Equal("yes", audit.Headers["X-MailStripper-Dispatch-Success"]);

        // Body assertions
        Assert.NotNull(audit.TextBody);
        Assert.Contains(expectedSha, audit.TextBody);
        Assert.Contains("Test Newsletter", audit.TextBody);
        Assert.Contains("SUCCESS", audit.TextBody);
        Assert.Contains("hxxps://example.com/unsub", audit.TextBody);

        Assert.NotNull(audit.HtmlBody);
        Assert.Contains(expectedSha, audit.HtmlBody);
        Assert.Contains("hxxps://example.com/unsub", audit.HtmlBody);
    }

    [Fact]
    public void GenerateAuditMessage_PoisonTrap_RendersThreatSignalsAndDefangsAttackerLinks()
    {
        var generator = new MimeAuditGenerator();
        var (original, rawBytes) = CreateDummyMessageWithBytes();

        var report = new ForensicReportDto(
            Classification: "PoisonOrReconTrap",
            Provenance: "UnverifiedForeignOrigin",
            RiskScore: 95,
            SummaryVerdict: "Deceptive opt-out anchor target pointing to malicious beacon.",
            ReconWarning: "Attacker reconnaissance token detected in URL parameter.",
            AuthservIdVerified: false,
            MatchedAuthservId: null,
            PrimaryTargetHost: "malicious-beacon.ru",
            SterileCurlCommand: null,
            PoisonSignals: new List<string>
            {
                "Punycode beacon host detected: xn--fake-mail.com",
                "Tracking pixel embedded in unsubscribe anchor target"
            },
            Candidates: new List<ForensicCandidateDto>
            {
                new("BodyAnchorText", "http://malicious-beacon.ru/track?id=victim123", "http", "malicious-beacon.ru", false, new List<string> { "DeceptiveAnchorText" })
            }
        );

        var context = new AuditContext(original, rawBytes, report, DispatchResult: null);
        var audit = generator.GenerateAuditMessage(context);

        Assert.Equal("PoisonOrReconTrap", audit.Headers["X-MailStripper-Classification"]);
        Assert.Equal("95", audit.Headers["X-MailStripper-Risk-Score"]);
        Assert.Null(audit.Headers["X-MailStripper-Dispatch-Status"]);

        // Body checks for defanged URLs and poison warnings
        Assert.Contains("Punycode beacon host detected", audit.TextBody);
        Assert.Contains("hxxp://malicious-beacon.ru/track?id=victim123", audit.TextBody);
        Assert.DoesNotContain("http://malicious-beacon.ru", audit.TextBody);

        Assert.Contains("hxxp://malicious-beacon.ru/track?id=victim123", audit.HtmlBody);
        Assert.DoesNotContain("http://malicious-beacon.ru", audit.HtmlBody);
    }

    [Theory]
    [InlineData("https://safe.example.com/opt-out", "hxxps://safe.example.com/opt-out")]
    [InlineData("http://insecure.example.com/link", "hxxp://insecure.example.com/link")]
    [InlineData("HTTPS://CAPS.EXAMPLE.COM/PATH", "hxxps://CAPS.EXAMPLE.COM/PATH")]
    [InlineData(null, "")]
    public void DefangUri_ProperlyNeutralizesSchemes(string? input, string expected)
    {
        var result = MimeAuditGenerator.DefangUri(input);
        Assert.Equal(expected, result);
    }
}
