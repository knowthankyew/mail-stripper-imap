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
    private readonly IImapFolderManager _folderManager;
    private readonly IImapClientFactory _clientFactory;
    private readonly IMimeAuditGenerator _auditGenerator;
    private readonly IBackoffPolicy? _backoffPolicy;
    private readonly ILogger<ImapWatcherService> _logger;

    public ImapWatcherService(
        IOptions<ImapOptions> options,
        IMailStripperClient mailStripperClient,
        ISterileUnsubscribeDispatcher dispatcher,
        IImapFolderManager folderManager,
        IImapClientFactory clientFactory,
        IMimeAuditGenerator auditGenerator,
        ILogger<ImapWatcherService> logger,
        IBackoffPolicy? backoffPolicy = null)
    {
        _options = options.Value;
        _mailStripperClient = mailStripperClient;
        _dispatcher = dispatcher;
        _folderManager = folderManager;
        _clientFactory = clientFactory;
        _auditGenerator = auditGenerator;
        _logger = logger;
        _backoffPolicy = backoffPolicy;
    }

    public ImapWatcherService(
        IOptions<ImapOptions> options,
        IMailStripperClient mailStripperClient,
        ISterileUnsubscribeDispatcher dispatcher,
        IImapFolderManager folderManager,
        IImapClientFactory clientFactory,
        ILogger<ImapWatcherService> logger,
        IBackoffPolicy? backoffPolicy = null)
        : this(options, mailStripperClient, dispatcher, folderManager, clientFactory, new MimeAuditGenerator(), logger, backoffPolicy)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MailStripper IMAP Watcher starting for mailbox '{Folder}' on {Host}:{Port}...",
            _options.MonitoredFolder, _options.Host, _options.Port);

        var backoff = _backoffPolicy ?? new ExponentialBackoffPolicy(
            TimeSpan.FromSeconds(_options.ReconnectInitialDelaySeconds),
            TimeSpan.FromSeconds(_options.ReconnectMaxDelaySeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var client = _clientFactory.CreateClient();
                await client.ConnectAsync(_options.Host, _options.Port, _options.UseSsl, stoppingToken);

                if (!string.IsNullOrEmpty(_options.Username))
                {
                    await client.AuthenticateAsync(_options.Username, _options.Password, stoppingToken);
                }

                // Provision folder hierarchy if missing (@Safe-Unsubscribe, Archive, Quarantine, Audit)
                _logger.LogInformation("Provisioning folder hierarchy on {Host}...", _options.Host);
                await _folderManager.ProvisionHierarchyAsync(client, _options, stoppingToken);

                var folder = await client.GetFolderAsync(_options.MonitoredFolder, stoppingToken);
                await folder.OpenAsync(FolderAccess.ReadWrite, stoppingToken);

                _logger.LogInformation("Connected and watching '{Folder}' (Total Messages: {Count})",
                    folder.FullName, folder.Count);

                // Initial sweep of pending messages
                var uids = await folder.SearchAsync(MailKit.Search.SearchQuery.All, stoppingToken);
                foreach (var uid in uids)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await TryProcessMessageAsync(folder, uid, stoppingToken);
                }

                var connectionEstablished = DateTimeOffset.UtcNow;

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
                        // Reset backoff once connection has demonstrated stability through an IDLE cycle
                        backoff.Reset();
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
                        await TryProcessMessageAsync(folder, uid, stoppingToken);
                    }

                    if (DateTimeOffset.UtcNow - connectionEstablished >= TimeSpan.FromMinutes(1))
                    {
                        backoff.Reset();
                    }
                }

                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var delay = backoff.NextDelay();
                _logger.LogError(ex, "IMAP Watcher encountered connection error (attempt {Attempt}). Retrying in {DelaySeconds:F1}s...",
                    backoff.CurrentAttempt, delay.TotalSeconds);
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("MailStripper IMAP Watcher stopped gracefully.");
    }

    private async Task TryProcessMessageAsync(IMailFolder folder, UniqueId uid, CancellationToken ct)
    {
        try
        {
            await ProcessSingleMessageAsync(folder, uid, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
                                   && ex is not IOException
                                   and not ImapProtocolException
                                   and not ServiceNotConnectedException)
        {
            _logger.LogError(ex, "Message {Uid} failed processing. Skipping to next message.", uid);
        }
    }

    public async Task ProcessSingleMessageAsync(IMailFolder folder, UniqueId uid, CancellationToken ct)
    {
        // Check message flags to prevent duplicate execution across reconnect sweeps
        var summaries = await folder.FetchAsync(new[] { uid }, MessageSummaryItems.Flags, ct);
        var summary = summaries?.FirstOrDefault();
        if (summary != null)
        {
            if (summary.Keywords.Contains(ImapKeywords.SafeUnsubscribed))
            {
                _logger.LogInformation("Message {Uid} already carries {Keyword}. Skipping re-processing.",
                    uid, ImapKeywords.SafeUnsubscribed);
                return;
            }
            if (summary.Keywords.Contains(ImapKeywords.SuspiciousTrap))
            {
                _logger.LogInformation("Message {Uid} already carries {Keyword}. Skipping re-processing.",
                    uid, ImapKeywords.SuspiciousTrap);
                return;
            }
        }

        // 1. Download message body into volatile memory stream (Zero Disk Writes)
        var mimeMessage = await folder.GetMessageAsync(uid, ct);
        using var memoryStream = new MemoryStream();
        mimeMessage.WriteTo(memoryStream);

        if (memoryStream.Length > _options.MaxMessageByteSize)
        {
            _logger.LogWarning("Message {Uid} exceeds size budget ({Size} bytes). Skipping.", uid, memoryStream.Length);
            return;
        }

        var rawBytes = memoryStream.ToArray();
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
            _logger.LogWarning("Message {Uid} is a Poison / Recon Trap ({Signals}). Tagging {Keyword} and moving to Quarantine.",
                uid, string.Join("; ", unsub.PoisonSignals), ImapKeywords.SuspiciousTrap);

            var quarantineFolder = await _folderManager.ResolveFolderAsync(folder, _options.QuarantineFolder, ct);
            if (quarantineFolder != null)
            {
                await _folderManager.MoveWithKeywordAsync(folder, uid, quarantineFolder, ImapKeywords.SuspiciousTrap, ct);
            }

            await EmitAuditReportAsync(folder, uid, mimeMessage, rawBytes, unsub, null, ct);
            return;
        }

        DispatchResult? dispatchResult = null;
        if (unsub.Classification == "OneClickPostCandidate")
        {
            var httpsCandidate = unsub.Candidates.FirstOrDefault(c =>
                c.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                c.Source.Equals("HeaderListUnsubscribe", StringComparison.OrdinalIgnoreCase));

            if (httpsCandidate != null && _options.AutoExecuteSterileUnsubscribe)
            {
                _logger.LogInformation("Auto-executing sterile RFC 8058 opt-out for {Uid} targeting host {Host}...",
                    uid, httpsCandidate.Host);

                dispatchResult = await _dispatcher.DispatchAsync(httpsCandidate.SanitizedUri, unsub, ct);
                if (dispatchResult.Success)
                {
                    _logger.LogInformation("Sterile opt-out succeeded (HTTP {Status}) for {Uid}. Tagging {Keyword} and moving to Archive.",
                        dispatchResult.StatusCode, uid, ImapKeywords.SafeUnsubscribed);

                    // Apply keyword immediately on source folder so that even if folder move encounters an error,
                    // future sweeps will not re-dispatch another opt-out POST
                    await _folderManager.TryAddCustomKeywordAsync(folder, uid, ImapKeywords.SafeUnsubscribed, ct);

                    var archiveFolder = await _folderManager.ResolveFolderAsync(folder, _options.ArchiveFolder, ct);
                    if (archiveFolder != null)
                    {
                        await _folderManager.MoveWithKeywordAsync(folder, uid, archiveFolder, ImapKeywords.SafeUnsubscribed, ct);
                    }
                }
                else
                {
                    _logger.LogWarning("Sterile opt-out failed for {Uid}: {Error}",
                        uid, dispatchResult.ErrorMessage);
                }
            }
        }

        // 4. In-Mailbox Audit Report Generation & Append (Milestone 3)
        await EmitAuditReportAsync(folder, uid, mimeMessage, rawBytes, unsub, dispatchResult, ct);
    }

    private async Task EmitAuditReportAsync(
        IMailFolder folder,
        UniqueId uid,
        MimeMessage mimeMessage,
        byte[] rawBytes,
        ForensicReportDto unsub,
        DispatchResult? dispatchResult,
        CancellationToken ct)
    {
        try
        {
            var auditFolder = await _folderManager.ResolveFolderAsync(folder, _options.AuditFolder, ct);
            if (auditFolder != null)
            {
                var auditContext = new AuditContext(mimeMessage, rawBytes, unsub, dispatchResult);
                var auditMessage = _auditGenerator.GenerateAuditMessage(auditContext);
                await _folderManager.AppendAuditMessageAsync(auditFolder, auditMessage, ct);
                await _folderManager.TryAddCustomKeywordAsync(folder, uid, ImapKeywords.AuditLogged, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
                                   && ex is not IOException
                                   and not ImapProtocolException
                                   and not ServiceNotConnectedException)
        {
            _logger.LogError(ex, "Failed to emit audit report for message {Uid}.", uid);
        }
    }
}
