using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MailStripper.Imap.Models;
using MailStripper.Imap.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailStripper.Imap.Tests;

public class HealthEndpointServiceTests
{
    private static int GetAvailablePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    [Fact]
    public async Task HealthEndpoint_WhenHealthy_Returns200_WithReportJson()
    {
        var port = GetAvailablePort();
        var options = Options.Create(new ImapOptions
        {
            HealthCheckEnabled = true,
            HealthCheckHost = "127.0.0.1",
            HealthCheckPort = port
        });

        var tracker = new ImapHealthTracker();
        tracker.ReportConnected();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new HealthEndpointService(options, tracker, NullLogger<HealthEndpointService>.Instance);

        await service.StartAsync(cts.Token);
        // Allow listener to bind
        await Task.Delay(100, cts.Token);

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{port}/healthz", cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("connected").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("healthy").GetBoolean());

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HealthEndpoint_WhenUnhealthy_Returns503_WithReportJson()
    {
        var port = GetAvailablePort();
        var options = Options.Create(new ImapOptions
        {
            HealthCheckEnabled = true,
            HealthCheckHost = "127.0.0.1",
            HealthCheckPort = port
        });

        var tracker = new ImapHealthTracker();
        tracker.ReportConnected();
        tracker.ReportDisconnected("IMAP connection timeout");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new HealthEndpointService(options, tracker, NullLogger<HealthEndpointService>.Instance);

        await service.StartAsync(cts.Token);
        await Task.Delay(100, cts.Token);

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{port}/readyz", cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", doc.RootElement.GetProperty("status").GetString());
        Assert.False(doc.RootElement.GetProperty("connected").GetBoolean());
        Assert.Equal("IMAP connection timeout", doc.RootElement.GetProperty("lastError").GetString());

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HealthEndpoint_Livez_Returns200_Alive()
    {
        var port = GetAvailablePort();
        var options = Options.Create(new ImapOptions
        {
            HealthCheckEnabled = true,
            HealthCheckHost = "127.0.0.1",
            HealthCheckPort = port
        });

        var tracker = new ImapHealthTracker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new HealthEndpointService(options, tracker, NullLogger<HealthEndpointService>.Instance);

        await service.StartAsync(cts.Token);
        await Task.Delay(100, cts.Token);

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{port}/livez", cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Alive", doc.RootElement.GetProperty("status").GetString());

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HealthEndpoint_UnknownPath_Returns404()
    {
        var port = GetAvailablePort();
        var options = Options.Create(new ImapOptions
        {
            HealthCheckEnabled = true,
            HealthCheckHost = "127.0.0.1",
            HealthCheckPort = port
        });

        var tracker = new ImapHealthTracker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new HealthEndpointService(options, tracker, NullLogger<HealthEndpointService>.Instance);

        await service.StartAsync(cts.Token);
        await Task.Delay(100, cts.Token);

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{port}/nonexistent", cts.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HealthEndpoint_HeadRequest_Returns200_WithEmptyBody()
    {
        var port = GetAvailablePort();
        var options = Options.Create(new ImapOptions
        {
            HealthCheckEnabled = true,
            HealthCheckHost = "127.0.0.1",
            HealthCheckPort = port
        });

        var tracker = new ImapHealthTracker();
        tracker.ReportConnected();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new HealthEndpointService(options, tracker, NullLogger<HealthEndpointService>.Instance);

        await service.StartAsync(cts.Token);
        await Task.Delay(100, cts.Token);

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, $"http://127.0.0.1:{port}/healthz");
        var response = await http.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Empty(body);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HealthEndpoint_PostMethod_Returns405MethodNotAllowed()
    {
        var port = GetAvailablePort();
        var options = Options.Create(new ImapOptions
        {
            HealthCheckEnabled = true,
            HealthCheckHost = "127.0.0.1",
            HealthCheckPort = port
        });

        var tracker = new ImapHealthTracker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new HealthEndpointService(options, tracker, NullLogger<HealthEndpointService>.Instance);

        await service.StartAsync(cts.Token);
        await Task.Delay(100, cts.Token);

        using var http = new HttpClient();
        var response = await http.PostAsync($"http://127.0.0.1:{port}/healthz", new StringContent("test"), cts.Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HealthEndpoint_WhenDisabled_DoesNotStartListener()
    {
        var port = GetAvailablePort();
        var options = Options.Create(new ImapOptions
        {
            HealthCheckEnabled = false,
            HealthCheckHost = "127.0.0.1",
            HealthCheckPort = port
        });

        var tracker = new ImapHealthTracker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var service = new HealthEndpointService(options, tracker, NullLogger<HealthEndpointService>.Instance);

        await service.StartAsync(cts.Token);
        await Task.Delay(50, cts.Token);

        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        await Assert.ThrowsAnyAsync<Exception>(() => http.GetAsync($"http://127.0.0.1:{port}/healthz", cts.Token));

        await service.StopAsync(CancellationToken.None);
    }
}
