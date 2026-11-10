using System.Security.Cryptography;
using System.Text;
using MailStripper.Imap.Models;
using MimeKit;
using MimeKit.Utils;

namespace MailStripper.Imap.Services;

public record AuditContext(
    MimeMessage OriginalMessage,
    byte[] OriginalRawBytes,
    ForensicReportDto ForensicReport,
    DispatchResult? DispatchResult
);

public interface IMimeAuditGenerator
{
    MimeMessage GenerateAuditMessage(AuditContext context);
}

public class MimeAuditGenerator : IMimeAuditGenerator
{
    public MimeMessage GenerateAuditMessage(AuditContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.OriginalMessage);
        ArgumentNullException.ThrowIfNull(context.ForensicReport);

        var sha256Hex = Convert.ToHexStringLower(SHA256.HashData(context.OriginalRawBytes));
        var report = context.ForensicReport;
        var original = context.OriginalMessage;
        var dispatch = context.DispatchResult;

        var auditMessage = new MimeMessage();
        auditMessage.From.Add(new MailboxAddress("MailStripper Privacy Watcher", "audit@mailstripper.local"));

        if (original.To.Count > 0)
        {
            auditMessage.To.AddRange(original.To);
        }
        else
        {
            auditMessage.To.Add(new MailboxAddress("Mailbox Operator", "operator@mailstripper.local"));
        }

        var origSubject = string.IsNullOrWhiteSpace(original.Subject) ? "(No Subject)" : original.Subject;
        auditMessage.Subject = $"[AUDIT] {report.Classification} ({report.RiskScore}/100): {origSubject}";
        auditMessage.Date = DateTimeOffset.UtcNow;
        auditMessage.MessageId = MimeUtils.GenerateMessageId();

        // Structured Forensic X-Headers
        auditMessage.Headers.Add("X-MailStripper-Audit-Version", "0.3.0");
        auditMessage.Headers.Add("X-MailStripper-Classification", report.Classification);
        auditMessage.Headers.Add("X-MailStripper-Risk-Score", report.RiskScore.ToString());
        auditMessage.Headers.Add("X-MailStripper-Provenance", report.Provenance);
        auditMessage.Headers.Add("X-MailStripper-Original-Message-Id", original.MessageId ?? "unknown");
        auditMessage.Headers.Add("X-MailStripper-Original-Sha256", sha256Hex);
        auditMessage.Headers.Add("X-MailStripper-Authserv-Verified", report.AuthservIdVerified ? "yes" : "no");

        if (!string.IsNullOrWhiteSpace(report.MatchedAuthservId))
        {
            auditMessage.Headers.Add("X-MailStripper-Matched-Authserv", report.MatchedAuthservId);
        }

        if (dispatch != null)
        {
            auditMessage.Headers.Add("X-MailStripper-Dispatch-Status", dispatch.StatusCode.ToString());
            auditMessage.Headers.Add("X-MailStripper-Dispatch-Success", dispatch.Success ? "yes" : "no");
            auditMessage.Headers.Add("X-MailStripper-Dispatch-Executed-At", dispatch.ExecutedAt.ToString("o"));
        }

        // Build defanged text & HTML representations
        var textBody = BuildPlainTextBody(original, report, dispatch, sha256Hex);
        var htmlBody = BuildHtmlBody(original, report, dispatch, sha256Hex);

        var bodyBuilder = new BodyBuilder
        {
            TextBody = textBody,
            HtmlBody = htmlBody
        };

        auditMessage.Body = bodyBuilder.ToMessageBody();
        return auditMessage;
    }

    public static string DefangUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return string.Empty;
        return uri
            .Replace("https://", "hxxps://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "hxxp://", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPlainTextBody(
        MimeMessage original,
        ForensicReportDto report,
        DispatchResult? dispatch,
        string sha256Hex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                 MAILSTRIPPER FORENSIC AUDIT REPORT (v0.3.0)                   ");
        sb.AppendLine("================================================================================");
        sb.AppendLine();
        sb.AppendLine("--- 1. ORIGINAL MESSAGE SUMMARY ---");
        sb.AppendLine($"From:       {original.From}");
        sb.AppendLine($"Subject:    {original.Subject ?? "(No Subject)"}");
        sb.AppendLine($"Date:       {original.Date:R}");
        sb.AppendLine($"Message-ID: {original.MessageId ?? "unknown"}");
        sb.AppendLine($"SHA-256:    {sha256Hex}");
        sb.AppendLine();
        sb.AppendLine("--- 2. FORENSIC CLASSIFICATION & RISK ---");
        sb.AppendLine($"Classification:  {report.Classification}");
        sb.AppendLine($"Risk Score:      {report.RiskScore} / 100");
        sb.AppendLine($"Provenance:      {report.Provenance}");
        sb.AppendLine($"Authserv Valid:  {(report.AuthservIdVerified ? "YES" : "NO")}");
        sb.AppendLine($"Matched MX:      {report.MatchedAuthservId ?? "(None)"}");
        sb.AppendLine($"Verdict:         {report.SummaryVerdict}");
        if (!string.IsNullOrWhiteSpace(report.ReconWarning))
        {
            sb.AppendLine($"Warning:         {report.ReconWarning}");
        }
        sb.AppendLine();

        sb.AppendLine("--- 3. POISON & RECONNAISSANCE SIGNALS ---");
        if (report.PoisonSignals.Count == 0)
        {
            sb.AppendLine("None detected. Zero deceptive anchor text or poison tokens found.");
        }
        else
        {
            foreach (var sig in report.PoisonSignals)
            {
                sb.AppendLine($"[!] {sig}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("--- 4. STERILE RFC 8058 OPT-OUT DISPATCH ---");
        if (dispatch == null)
        {
            sb.AppendLine("Status: Not Executed (Dry-Run Mode or Non-Qualifying Classification)");
        }
        else
        {
            sb.AppendLine($"Executed:    {(dispatch.Success ? "SUCCESS" : "FAILED")}");
            sb.AppendLine($"HTTP Status: {dispatch.StatusCode}");
            sb.AppendLine($"Target:      {DefangUri(dispatch.TargetUri)}");
            sb.AppendLine($"Timestamp:   {dispatch.ExecutedAt:O}");
            sb.AppendLine("Guarantees:  UseCookies=false · AllowAutoRedirect=false · Immediate Content Disposal");
            if (!string.IsNullOrWhiteSpace(dispatch.ErrorMessage))
            {
                sb.AppendLine($"Error:       {dispatch.ErrorMessage}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("--- 5. ANALYZED OPT-OUT CANDIDATES ---");
        if (report.Candidates.Count == 0)
        {
            sb.AppendLine("No unsubscribe candidates found in headers or body.");
        }
        else
        {
            for (int i = 0; i < report.Candidates.Count; i++)
            {
                var c = report.Candidates[i];
                sb.AppendLine($"[{i + 1}] Source:  {c.Source}");
                sb.AppendLine($"    Target:  {DefangUri(c.SanitizedUri)}");
                sb.AppendLine($"    Host:    {c.Host}");
                sb.AppendLine($"    Aligned: {(c.AlignedWithSenderOrEsp ? "YES" : "NO")}");
                if (c.RiskFlags.Count > 0)
                {
                    sb.AppendLine($"    Flags:   {string.Join(", ", c.RiskFlags)}");
                }
            }
        }
        sb.AppendLine();
        sb.AppendLine("================================================================================");
        sb.AppendLine("   Generated by mail-stripper-imap · 100% Volatile RAM Ingestion Guarantee     ");
        sb.AppendLine("================================================================================");
        return sb.ToString();
    }

    private static string BuildHtmlBody(
        MimeMessage original,
        ForensicReportDto report,
        DispatchResult? dispatch,
        string sha256Hex)
    {
        var badgeColor = report.RiskScore switch
        {
            >= 75 => "#dc2626", // Red for high risk
            >= 35 => "#d97706", // Amber for moderate risk
            _ => "#16a34a"      // Green for low risk
        };

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset='utf-8'/><style>");
        sb.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; line-height: 1.5; color: #1e293b; background-color: #f8fafc; margin: 0; padding: 24px; }");
        sb.AppendLine(".card { background: #ffffff; border: 1px solid #e2e8f0; border-radius: 8px; padding: 24px; max-width: 680px; margin: 0 auto; box-shadow: 0 1px 3px rgba(0,0,0,0.05); }");
        sb.AppendLine(".header { border-bottom: 2px solid #e2e8f0; padding-bottom: 16px; margin-bottom: 20px; }");
        sb.AppendLine(".badge { display: inline-block; padding: 4px 12px; border-radius: 9999px; font-weight: 700; font-size: 13px; color: #ffffff; }");
        sb.AppendLine("h2 { margin: 0 0 8px 0; font-size: 20px; color: #0f172a; }");
        sb.AppendLine("h3 { margin: 20px 0 8px 0; font-size: 15px; color: #475569; text-transform: uppercase; letter-spacing: 0.05em; border-bottom: 1px solid #f1f5f9; padding-bottom: 4px; }");
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 8px; font-size: 14px; }");
        sb.AppendLine("td { padding: 6px 0; vertical-align: top; }");
        sb.AppendLine(".label { width: 140px; color: #64748b; font-weight: 500; }");
        sb.AppendLine("code { font-family: ui-monospace, Menlo, Monaco, 'Courier New', monospace; font-size: 12px; background: #f1f5f9; padding: 2px 6px; border-radius: 4px; color: #0f172a; word-break: break-all; }");
        sb.AppendLine(".alert { background: #fef2f2; border: 1px solid #fecaca; color: #991b1b; padding: 12px; border-radius: 6px; margin-top: 8px; font-size: 13px; }");
        sb.AppendLine(".success { background: #f0fdf4; border: 1px solid #bbf7d0; color: #166534; padding: 12px; border-radius: 6px; margin-top: 8px; font-size: 13px; }");
        sb.AppendLine(".footer { margin-top: 24px; font-size: 12px; color: #94a3b8; text-align: center; border-top: 1px solid #e2e8f0; padding-top: 12px; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<div class='card'>");
        sb.AppendLine("<div class='header'>");
        sb.AppendLine($"<span class='badge' style='background-color: {badgeColor};'>{report.Classification} · Risk {report.RiskScore}/100</span>");
        sb.AppendLine($"<h2 style='margin-top: 12px;'>{System.Net.WebUtility.HtmlEncode(original.Subject ?? "(No Subject)")}</h2>");
        sb.AppendLine($"<div style='color: #64748b; font-size: 13px;'>From: {System.Net.WebUtility.HtmlEncode(original.From.ToString())}</div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<h3>Original Message Metadata</h3>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><td class='label'>Date:</td><td>{original.Date:R}</td></tr>");
        sb.AppendLine($"<tr><td class='label'>Message-ID:</td><td><code>{System.Net.WebUtility.HtmlEncode(original.MessageId ?? "unknown")}</code></td></tr>");
        sb.AppendLine($"<tr><td class='label'>SHA-256 Digest:</td><td><code>{sha256Hex}</code></td></tr>");
        sb.AppendLine("</table>");

        sb.AppendLine("<h3>Forensic Analysis Verdict</h3>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><td class='label'>Provenance:</td><td>{System.Net.WebUtility.HtmlEncode(report.Provenance)}</td></tr>");
        sb.AppendLine($"<tr><td class='label'>Receiving MX:</td><td>{(report.AuthservIdVerified ? "Verified" : "Unverified")} ({System.Net.WebUtility.HtmlEncode(report.MatchedAuthservId ?? "None")})</td></tr>");
        sb.AppendLine($"<tr><td class='label'>Summary Verdict:</td><td>{System.Net.WebUtility.HtmlEncode(report.SummaryVerdict)}</td></tr>");
        sb.AppendLine("</table>");

        if (report.PoisonSignals.Count > 0)
        {
            sb.AppendLine("<h3>Poison & Reconnaissance Signals</h3>");
            sb.AppendLine("<div class='alert'>");
            sb.AppendLine("<strong>Identified Threat Traps:</strong><ul style='margin: 4px 0 0 0; padding-left: 20px;'>");
            foreach (var sig in report.PoisonSignals)
            {
                sb.AppendLine($"<li>{System.Net.WebUtility.HtmlEncode(sig)}</li>");
            }
            sb.AppendLine("</ul></div>");
        }

        sb.AppendLine("<h3>Sterile RFC 8058 Opt-Out Execution</h3>");
        if (dispatch == null)
        {
            sb.AppendLine("<p style='font-size: 13px; color: #64748b; font-style: italic;'>Not Executed: Operating in dry-run mode or message did not qualify for automated sterile unsubscribe.</p>");
        }
        else if (dispatch.Success)
        {
            sb.AppendLine("<div class='success'>");
            sb.AppendLine($"<strong>Opt-Out Succeeded (HTTP {dispatch.StatusCode})</strong><br/>");
            sb.AppendLine($"Target: <code>{System.Net.WebUtility.HtmlEncode(DefangUri(dispatch.TargetUri))}</code><br/>");
            sb.AppendLine($"Executed: {dispatch.ExecutedAt:O}<br/>");
            sb.AppendLine("Guarantees: Zero cookies · No redirects · Immediate stream disposal");
            sb.AppendLine("</div>");
        }
        else
        {
            sb.AppendLine("<div class='alert'>");
            sb.AppendLine($"<strong>Opt-Out Failed (HTTP {dispatch.StatusCode})</strong><br/>");
            sb.AppendLine($"Target: <code>{System.Net.WebUtility.HtmlEncode(DefangUri(dispatch.TargetUri))}</code><br/>");
            sb.AppendLine($"Error: {System.Net.WebUtility.HtmlEncode(dispatch.ErrorMessage ?? "Unknown Error")}");
            sb.AppendLine("</div>");
        }

        if (report.Candidates.Count > 0)
        {
            sb.AppendLine("<h3>Analyzed Unsubscribe Candidates</h3>");
            sb.AppendLine("<table>");
            foreach (var c in report.Candidates)
            {
                sb.AppendLine($"<tr><td class='label'>{System.Net.WebUtility.HtmlEncode(c.Source)}:</td><td><code>{System.Net.WebUtility.HtmlEncode(DefangUri(c.SanitizedUri))}</code></td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<div class='footer'>mail-stripper-imap · 100% Volatile RAM Ingestion · Zero Disk Writes</div>");
        sb.AppendLine("</div></body></html>");

        return sb.ToString();
    }
}
