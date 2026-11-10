using System.Net;
using MailStripper.Imap.Models;
using MailStripper.Imap.Services;
using Xunit;

namespace MailStripper.Imap.Tests;

public class SterileDispatcherTests
{
    private static ForensicReportDto CreateValidReport(string targetUri = "https://optout.example.com/one-click") =>
        new(
            Classification: "OneClickPostCandidate",
            Provenance: "TrustedReceivingMx",
            RiskScore: 10,
            SummaryVerdict: "Valid candidate.",
            ReconWarning: "Recon warning.",
            AuthservIdVerified: true,
            MatchedAuthservId: "mx.local",
            PrimaryTargetHost: "optout.example.com",
            SterileCurlCommand: "curl ...",
            PoisonSignals: new List<string>(),
            Candidates: new List<ForensicCandidateDto>
            {
                new("HeaderListUnsubscribe", targetUri, "https", "optout.example.com", true, new List<string>())
            }
        );

    [Fact]
    public async Task DispatchAsync_RefusesNonHttpsSchemeFailClosed()
    {
        var dispatcher = new SterileUnsubscribeDispatcher();
        var report = CreateValidReport();

        var result = await dispatcher.DispatchAsync("http://insecure.example/unsub", report);

        Assert.False(result.Success);
        Assert.Equal(0, result.StatusCode);
        Assert.Contains("https://", result.ErrorMessage);
    }

    [Fact]
    public async Task DispatchAsync_RefusesWhenPoisonSignalsArePresent()
    {
        var dispatcher = new SterileUnsubscribeDispatcher();
        var report = CreateValidReport() with
        {
            PoisonSignals = new List<string> { "Deceptive anchor text detected." }
        };

        var result = await dispatcher.DispatchAsync("https://optout.example.com/trap", report);

        Assert.False(result.Success);
        Assert.Equal(0, result.StatusCode);
        Assert.Contains("poison signals", result.ErrorMessage);
    }

    [Fact]
    public async Task DispatchAsync_RefusesWhenClassifiedAsPoisonOrReconTrap()
    {
        var dispatcher = new SterileUnsubscribeDispatcher();
        var report = CreateValidReport() with
        {
            Classification = "PoisonOrReconTrap"
        };

        var result = await dispatcher.DispatchAsync("https://optout.example.com/trap", report);

        Assert.False(result.Success);
        Assert.Equal(0, result.StatusCode);
        Assert.Contains("PoisonOrReconTrap", result.ErrorMessage);
    }

    [Fact]
    public async Task DispatchAsync_SetsRfc8058BodyAndDiscardsResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var fakeHandler = new FakeHttpMessageHandler((req) =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Unsubscribed successfully.")
            };
        });

        using var httpClient = new HttpClient(fakeHandler);
        var dispatcher = new SterileUnsubscribeDispatcher(httpClient);
        var report = CreateValidReport();

        var result = await dispatcher.DispatchAsync("https://optout.systems-digest.io/one-click", report);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://optout.systems-digest.io/one-click", capturedRequest.RequestUri?.ToString());
        Assert.Equal("List-Unsubscribe=One-Click", capturedBody);
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
