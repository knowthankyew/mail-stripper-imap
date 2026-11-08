using System.Text;
using MailStripper.Imap.Models;

namespace MailStripper.Imap.Services;

public record DispatchResult(
    bool Success,
    int StatusCode,
    string TargetUri,
    DateTimeOffset ExecutedAt,
    string? ErrorMessage
);

public interface ISterileUnsubscribeDispatcher
{
    Task<DispatchResult> DispatchAsync(
        string targetUri,
        ForensicReportDto report,
        CancellationToken ct = default);
}

public class SterileUnsubscribeDispatcher : ISterileUnsubscribeDispatcher
{
    private readonly HttpClient _sterileHttpClient;

    public SterileUnsubscribeDispatcher()
        : this(CreateDefaultSterileClient())
    {
    }

    internal SterileUnsubscribeDispatcher(HttpClient customClient)
    {
        _sterileHttpClient = customClient ?? throw new ArgumentNullException(nameof(customClient));
    }

    private static HttpClient CreateDefaultSterileClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1)
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task<DispatchResult> DispatchAsync(
        string targetUri,
        ForensicReportDto report,
        CancellationToken ct = default)
    {
        var executedAt = DateTimeOffset.UtcNow;

        if (report.PoisonSignals.Count > 0)
        {
            return new DispatchResult(false, 0, targetUri, executedAt,
                $"Refused: Message contains {report.PoisonSignals.Count} active poison signals.");
        }

        if (!Uri.TryCreate(targetUri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return new DispatchResult(false, 0, targetUri, executedAt,
                "Refused: RFC 8058 sterile execution requires https:// scheme.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent("List-Unsubscribe=One-Click", Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        try
        {
            using var response = await _sterileHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            // Immediately dispose content stream to drop any remote response payload
            response.Content.Dispose();

            int code = (int)response.StatusCode;
            bool success = code >= 200 && code < 300;
            return new DispatchResult(
                success,
                code,
                targetUri,
                executedAt,
                success ? null : $"Remote server returned status HTTP {code}"
            );
        }
        catch (Exception ex)
        {
            return new DispatchResult(false, 0, targetUri, executedAt, ex.Message);
        }
    }
}
