using System.Net.Http.Json;
using MailStripper.Imap.Models;

namespace MailStripper.Imap.Services;

public interface IMailStripperClient
{
    Task<StripFileResponse?> AnalyzeMessageStreamAsync(
        Stream rawMessageStream,
        string fileName = "message.eml",
        string? expectedAuthservId = null,
        CancellationToken ct = default);
}

public class MailStripperClient : IMailStripperClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public MailStripperClient(HttpClient httpClient, string baseUrl = "http://localhost:5001")
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<StripFileResponse?> AnalyzeMessageStreamAsync(
        Stream rawMessageStream,
        string fileName = "message.eml",
        string? expectedAuthservId = null,
        CancellationToken ct = default)
    {
        if (rawMessageStream.CanSeek)
        {
            rawMessageStream.Position = 0;
        }

        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(rawMessageStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("message/rfc822");
        content.Add(streamContent, "file", fileName);

        if (!string.IsNullOrWhiteSpace(expectedAuthservId))
        {
            content.Add(new StringContent(expectedAuthservId), "authservId");
        }

        var endpoint = $"{_baseUrl}/api/strip/file";
        var response = await _httpClient.PostAsync(endpoint, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"mailStripper returned status {response.StatusCode}: {errBody}");
        }

        return await response.Content.ReadFromJsonAsync<StripFileResponse>(cancellationToken: ct);
    }
}
