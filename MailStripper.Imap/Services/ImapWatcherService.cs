using MailKit;
using MailKit.Net.Imap;
using MailStripper.Imap.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MailStripper.Imap.Services;

public class ImapWatcherService : BackgroundService
{
    private readonly ImapOptions _options;
    private readonly IMailStripperClient _mailStripperClient;
    private readonly ISterileUnsubscribeDispatcher _dispatcher;
    private readonly ILogger<ImapWatcherService> _logger;

    public ImapWatcherService(
        IOptions<ImapOptions> options,
        IMailStripperClient mailStripperClient,
        ISterileUnsubscribeDispatcher dispatcher,
        ILogger<ImapWatcherService> logger)
    {
        _options = options.Value;
        _mailStripperClient = mailStripperClient;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MailStripper IMAP Watcher starting for mailbox '{Folder}' on {Host}:{Port}...",
            _options.MonitoredFolder, _options.Host, _options.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var client = new ImapClient();
                await client.ConnectAsync(_options.Host, _options.Port, _options.UseSsl, stoppingToken);

                if (!string.IsNullOrEmpty(_options.Username))
                {
                    await client.AuthenticateAsync(_options.Username, _options.Password, stoppingToken);
                }

                var folder = await client.GetFolderAsync(_options.MonitoredFolder, stoppingToken);
                await folder.OpenAsync(FolderAccess.ReadWrite, stoppingToken);

                _logger.LogInformation("Connected and watching '{Folder}' (Total Messages: {Count})",
                    folder.FullName, folder.Count);

                // Initial sweep of pending messages
                var uids = await folder.SearchAsync(MailKit.Search.SearchQuery.All, stoppingToken);
                foreach (var uid in uids)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await ProcessSingleMessageAsync(folder, uid, stoppingToken);
                }

                // IDLE loop: RFC 2177 requires ending IDLE before issuing folder commands
                while (!stoppingToken.IsCancellationRequested && client.IsConnected)
                {
                    using var done = new CancellationTokenSource(TimeSpan.FromMinutes(9));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, done.Token);

                    void OnCountChanged(object? sender, EventArgs e)
                    {
                        try
                        {
                            done.Cancel();
                        }
                        catch (ObjectDisposedException) { }
                    }

                    folder.CountChanged += OnCountChanged;
                    try
                    {
                        await client.IdleAsync(linked.Token);
                    }
                    finally
                    {
                        folder.CountChanged -= OnCountChanged;
                    }

                    if (stoppingToken.IsCancellationRequested) break;

                    // Post-IDLE sweep for newly arrived messages
                    var newUids = await folder.SearchAsync(MailKit.Search.SearchQuery.New, stoppingToken);
                    foreach (var uid in newUids)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        await ProcessSingleMessageAsync(folder, uid, stoppingToken);
                    }
                }

                await client.DisconnectAsync(true, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IMAP Watcher encountered connection error. Retrying in 15 seconds...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("MailStripper IMAP Watcher stopped gracefully.");
    }

    public async Task ProcessSingleMessageAsync(IMailFolder folder, UniqueId uid, CancellationToken ct)
    {
        // 1. Download message body into volatile memory stream (Zero Disk Writes)
        var mimeMessage = await folder.GetMessageAsync(uid, ct);
        using var memoryStream = new MemoryStream();
        mimeMessage.WriteTo(memoryStream);

        if (memoryStream.Length > _options.MaxMessageByteSize)
        {
            _logger.LogWarning("Message {Uid} exceeds size budget ({Size} bytes). Skipping.", uid, memoryStream.Length);
            return;
        }

        memoryStream.Position = 0;

        // 2. Transmit raw stream to local mailStripper (:5001)
        var report = await _mailStripperClient.AnalyzeMessageStreamAsync(
            memoryStream,
            fileName: $"msg-{uid}.eml",
            expectedAuthservId: _options.TrustedAuthservId,
            ct: ct);

        if (report?.Unsubscribe == null)
        {
            _logger.LogInformation("Message {Uid} analyzed: No unsubscribe forensic data returned.", uid);
            return;
        }

        var unsub = report.Unsubscribe;
        _logger.LogInformation("Message {Uid} verdict: {Classification} (Risk: {Risk}, Provenance: {Provenance})",
            uid, unsub.Classification, unsub.RiskScore, unsub.Provenance);

        // 3. Automated Segregation & Handling
        if (unsub.Classification == "PoisonOrReconTrap")
        {
            _logger.LogWarning("Message {Uid} is a Poison / Recon Trap ({Signals}). Moving to Quarantine.",
                uid, string.Join("; ", unsub.PoisonSignals));

            var quarantineFolder = await ResolveFolderAsync(folder, _options.QuarantineFolder, ct);
            if (quarantineFolder != null)
            {
                await folder.MoveToAsync(uid, quarantineFolder, ct);
            }
            return;
        }

        if (unsub.Classification == "OneClickPostCandidate")
        {
            var httpsCandidate = unsub.Candidates.FirstOrDefault(c =>
                c.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                c.Source.Equals("HeaderListUnsubscribe", StringComparison.OrdinalIgnoreCase));

            if (httpsCandidate != null && _options.AutoExecuteSterileUnsubscribe)
            {
                _logger.LogInformation("Auto-executing sterile RFC 8058 opt-out for {Uid} targeting {Uri}...",
                    uid, httpsCandidate.SanitizedUri);

                var dispatchResult = await _dispatcher.DispatchAsync(httpsCandidate.SanitizedUri, unsub, ct);
                if (dispatchResult.Success)
                {
                    _logger.LogInformation("Sterile opt-out succeeded (HTTP {Status}) for {Uid}.",
                        dispatchResult.StatusCode, uid);
                }
                else
                {
                    _logger.LogWarning("Sterile opt-out failed for {Uid}: {Error}",
                        uid, dispatchResult.ErrorMessage);
                }
            }

            var archiveFolder = await ResolveFolderAsync(folder, _options.ArchiveFolder, ct);
            if (archiveFolder != null)
            {
                await folder.MoveToAsync(uid, archiveFolder, ct);
            }
        }
    }

    private static async Task<IMailFolder?> ResolveFolderAsync(IMailFolder currentFolder, string targetPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        var sep = currentFolder.DirectorySeparator;
        var parts = targetPath.Split(new[] { '/', '.', sep }, StringSplitOptions.RemoveEmptyEntries);

        // Traverse to root parent folder
        IMailFolder root = currentFolder;
        while (root.ParentFolder != null)
        {
            root = root.ParentFolder;
        }

        IMailFolder? curr = root;
        foreach (var part in parts)
        {
            if (curr == null) break;
            curr = await curr.GetSubfolderAsync(part, ct);
        }

        return curr;
    }
}
