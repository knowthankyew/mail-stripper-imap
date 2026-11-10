using System.Net;
using System.Text;
using System.Text.Json;
using MailStripper.Imap.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailStripper.Imap.Services;

public class HealthEndpointService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ImapOptions _options;
    private readonly IImapHealthTracker _tracker;
    private readonly ILogger<HealthEndpointService> _logger;
    private HttpListener? _listener;

    public HealthEndpointService(
        IOptions<ImapOptions> options,
        IImapHealthTracker tracker,
        ILogger<HealthEndpointService> logger)
    {
        _options = options.Value;
        _tracker = tracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.HealthCheckEnabled)
        {
            _logger.LogInformation("Health endpoint is disabled by configuration.");
            return;
        }

        var host = string.IsNullOrWhiteSpace(_options.HealthCheckHost) ? "*" : _options.HealthCheckHost;
        var prefix = $"http://{host}:{_options.HealthCheckPort}/";

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _logger.LogInformation("Health endpoint listening on {Prefix} (probes: /healthz, /readyz, /livez)", prefix);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize health endpoint listener on {Prefix}", prefix);
            return;
        }

        using var registration = stoppingToken.Register(() =>
        {
            try { _listener?.Stop(); } catch { }
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = ProcessRequestAsync(context);
            }
            catch (HttpListenerException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Transient error accepting connection on health endpoint");
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }

        _logger.LogInformation("Health endpoint listener stopped.");
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod is not ("GET" or "HEAD"))
            {
                context.Response.StatusCode = 405;
                context.Response.Headers.Add("Allow", "GET, HEAD");
                return;
            }

            var rawPath = context.Request.Url?.AbsolutePath ?? "/";
            var path = rawPath.TrimEnd('/').ToLowerInvariant();
            if (string.IsNullOrEmpty(path)) path = "/";

            var isHead = context.Request.HttpMethod == "HEAD";

            if (path is "/healthz" or "/health" or "/readyz")
            {
                var isHealthy = _tracker.IsHealthy;
                var report = _tracker.GetReport();
                var json = JsonSerializer.Serialize(report, JsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);

                context.Response.StatusCode = isHealthy ? 200 : 503;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                if (!isHead)
                {
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
            }
            else if (path is "/livez" or "/ping")
            {
                var bytes = Encoding.UTF8.GetBytes("{\"status\":\"Alive\"}");
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                if (!isHead)
                {
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes("Not Found");
                context.Response.StatusCode = 404;
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                if (!isHead)
                {
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing health check request");
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch { }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        return base.StopAsync(cancellationToken);
    }
}
