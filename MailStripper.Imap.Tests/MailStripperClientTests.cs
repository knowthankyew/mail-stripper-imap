using System.Net;
using System.Text;
using System.Text.Json;
using MailStripper.Imap.Models;
using MailStripper.Imap.Services;
using Xunit;

namespace MailStripper.Imap.Tests;

public class MailStripperClientTests
{
    [Fact]
    public async Task AnalyzeMessageStreamAsync_StreamsRawBytesAndDeserializesReport()
    {
        var expectedResponse = new StripFileResponse(
            Id: "msg-456",
            OneSentenceTitle: "Newsletter digest.",
            ShortSummary: "Summary text.",
            Unsubscribe: new ForensicReportDto(
                Classification: "OneClickPostCandidate",
                Provenance: "TrustedReceivingMx",
                RiskScore: 10,
                SummaryVerdict: "Conforming opt-out.",
                ReconWarning: "Warning text.",
                AuthservIdVerified: true,
                MatchedAuthservId: "mx.local",
                PrimaryTargetHost: "systems-digest.io",
                SterileCurlCommand: "curl ...",
                PoisonSignals: new List<string>(),
                Candidates: new List<ForensicCandidateDto>()
            )
        );

        HttpRequestMessage? capturedRequest = null;
        var fakeHandler = new FakeHttpMessageHandler((req) =>
        {
            capturedRequest = req;
            var json = JsonSerializer.Serialize(expectedResponse);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(fakeHandler);
        var client = new MailStripperClient(httpClient, "http://localhost:5001");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("From: digest@systems-digest.io\r\nSubject: Test\r\n\r\nBody."));
        var result = await client.AnalyzeMessageStreamAsync(stream, "test.eml", expectedAuthservId: "mx.local");

        Assert.NotNull(result);
        Assert.Equal("msg-456", result.Id);
        Assert.Equal("OneClickPostCandidate", result.Unsubscribe?.Classification);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("http://localhost:5001/api/strip/file", capturedRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task AnalyzeMessageStreamAsync_ThrowsOnNonSuccessStatus()
    {
        var fakeHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"Invalid MIME message\"}", Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(fakeHandler);
        var client = new MailStripperClient(httpClient, "http://localhost:5001");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Invalid raw payload"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.AnalyzeMessageStreamAsync(stream, "invalid.eml"));
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
