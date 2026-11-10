using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailStripper.Imap.Models;
using MailStripper.Imap.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailStripper.Imap.Tests;

public class ImapWatcherServiceTests
{
    private static MimeMessage CreateDummyMessage(string subject = "Test Subject", string body = "Test Body")
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        msg.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        msg.Subject = subject;
        msg.Body = new TextPart("plain") { Text = body };
        return msg;
    }

    private static ForensicReportDto CreateForensicReport(string classification, List<string>? poisonSignals = null, string targetUri = "https://example.com/unsub")
    {
        return new ForensicReportDto(
            Classification: classification,
            Provenance: "TrustedReceivingMx",
            RiskScore: classification == "PoisonOrReconTrap" ? 95 : 10,
            SummaryVerdict: "Test Verdict",
            ReconWarning: "Warning",
            AuthservIdVerified: true,
            MatchedAuthservId: "mx.local",
            PrimaryTargetHost: "example.com",
            SterileCurlCommand: "curl ...",
            PoisonSignals: poisonSignals ?? new List<string>(),
            Candidates: new List<ForensicCandidateDto>
            {
                new("HeaderListUnsubscribe", targetUri, "https", "example.com", true, new List<string>())
            }
        );
    }

    [Fact]
    public async Task ProcessSingleMessageAsync_PoisonTrap_TagsSuspiciousTrapAndMovesToQuarantine()
    {
        var uid = new UniqueId(101);
        var folder = Substitute.For<IMailFolder>();
        var quarantineFolder = Substitute.For<IMailFolder>();
        folder.GetMessageAsync(uid, Arg.Any<CancellationToken>()).Returns(CreateDummyMessage());

        var mailStripperClient = Substitute.For<IMailStripperClient>();
        var poisonReport = CreateForensicReport("PoisonOrReconTrap", new List<string> { "Beacon URL detected" });
        mailStripperClient.AnalyzeMessageStreamAsync(
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(new StripFileResponse("id1", "Title", "Summary", poisonReport));

        var dispatcher = Substitute.For<ISterileUnsubscribeDispatcher>();
        var folderManager = Substitute.For<IImapFolderManager>();
        var clientFactory = Substitute.For<IImapClientFactory>();

        folderManager.ResolveFolderAsync(folder, "@Safe-Unsubscribe/Quarantine", Arg.Any<CancellationToken>())
            .Returns(quarantineFolder);

        var options = Options.Create(new ImapOptions
        {
            AutoExecuteSterileUnsubscribe = true,
            QuarantineFolder = "@Safe-Unsubscribe/Quarantine"
        });

        var service = new ImapWatcherService(
            options,
            mailStripperClient,
            dispatcher,
            folderManager,
            clientFactory,
            NullLogger<ImapWatcherService>.Instance);

        await service.ProcessSingleMessageAsync(folder, uid, CancellationToken.None);

        // Verify moved to quarantine with $SuspiciousTrap
        await folderManager.Received(1).MoveWithKeywordAsync(
            folder,
            uid,
            quarantineFolder,
            ImapKeywords.SuspiciousTrap,
            Arg.Any<CancellationToken>());

        // Verify dispatcher was never invoked
        await dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<ForensicReportDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSingleMessageAsync_ValidCandidate_AutoExecuteTrue_DispatchesAndMovesToArchive()
    {
        var uid = new UniqueId(202);
        var folder = Substitute.For<IMailFolder>();
        var archiveFolder = Substitute.For<IMailFolder>();
        folder.GetMessageAsync(uid, Arg.Any<CancellationToken>()).Returns(CreateDummyMessage());

        var mailStripperClient = Substitute.For<IMailStripperClient>();
        var candidateReport = CreateForensicReport("OneClickPostCandidate", targetUri: "https://example.com/one-click");
        mailStripperClient.AnalyzeMessageStreamAsync(
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(new StripFileResponse("id2", "Title", "Summary", candidateReport));

        var dispatcher = Substitute.For<ISterileUnsubscribeDispatcher>();
        dispatcher.DispatchAsync("https://example.com/one-click", candidateReport, Arg.Any<CancellationToken>())
            .Returns(new DispatchResult(true, 200, "https://example.com/one-click", DateTimeOffset.UtcNow, null));

        var folderManager = Substitute.For<IImapFolderManager>();
        var clientFactory = Substitute.For<IImapClientFactory>();

        folderManager.ResolveFolderAsync(folder, "@Safe-Unsubscribe/Archive", Arg.Any<CancellationToken>())
            .Returns(archiveFolder);

        var options = Options.Create(new ImapOptions
        {
            AutoExecuteSterileUnsubscribe = true,
            ArchiveFolder = "@Safe-Unsubscribe/Archive"
        });

        var service = new ImapWatcherService(
            options,
            mailStripperClient,
            dispatcher,
            folderManager,
            clientFactory,
            NullLogger<ImapWatcherService>.Instance);

        await service.ProcessSingleMessageAsync(folder, uid, CancellationToken.None);

        // Verify dispatched
        await dispatcher.Received(1).DispatchAsync("https://example.com/one-click", candidateReport, Arg.Any<CancellationToken>());

        // Verify moved to archive with $SafeUnsubscribed
        await folderManager.Received(1).MoveWithKeywordAsync(
            folder,
            uid,
            archiveFolder,
            ImapKeywords.SafeUnsubscribed,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSingleMessageAsync_ValidCandidate_AutoExecuteFalse_DoesNotDispatchOrMove()
    {
        var uid = new UniqueId(303);
        var folder = Substitute.For<IMailFolder>();
        folder.GetMessageAsync(uid, Arg.Any<CancellationToken>()).Returns(CreateDummyMessage());

        var mailStripperClient = Substitute.For<IMailStripperClient>();
        var candidateReport = CreateForensicReport("OneClickPostCandidate");
        mailStripperClient.AnalyzeMessageStreamAsync(
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(new StripFileResponse("id3", "Title", "Summary", candidateReport));

        var dispatcher = Substitute.For<ISterileUnsubscribeDispatcher>();
        var folderManager = Substitute.For<IImapFolderManager>();
        var clientFactory = Substitute.For<IImapClientFactory>();

        var options = Options.Create(new ImapOptions
        {
            AutoExecuteSterileUnsubscribe = false // Dry-run mode
        });

        var service = new ImapWatcherService(
            options,
            mailStripperClient,
            dispatcher,
            folderManager,
            clientFactory,
            NullLogger<ImapWatcherService>.Instance);

        await service.ProcessSingleMessageAsync(folder, uid, CancellationToken.None);

        // Verify no dispatch
        await dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<ForensicReportDto>(),
            Arg.Any<CancellationToken>());

        // Verify no move
        await folderManager.DidNotReceive().MoveWithKeywordAsync(
            Arg.Any<IMailFolder>(),
            Arg.Any<UniqueId>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSingleMessageAsync_DispatchFails_DoesNotMoveToArchive()
    {
        var uid = new UniqueId(404);
        var folder = Substitute.For<IMailFolder>();
        folder.GetMessageAsync(uid, Arg.Any<CancellationToken>()).Returns(CreateDummyMessage());

        var mailStripperClient = Substitute.For<IMailStripperClient>();
        var candidateReport = CreateForensicReport("OneClickPostCandidate", targetUri: "https://example.com/one-click");
        mailStripperClient.AnalyzeMessageStreamAsync(
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(new StripFileResponse("id4", "Title", "Summary", candidateReport));

        var dispatcher = Substitute.For<ISterileUnsubscribeDispatcher>();
        dispatcher.DispatchAsync("https://example.com/one-click", candidateReport, Arg.Any<CancellationToken>())
            .Returns(new DispatchResult(false, 500, "https://example.com/one-click", DateTimeOffset.UtcNow, "Remote server 500"));

        var folderManager = Substitute.For<IImapFolderManager>();
        var clientFactory = Substitute.For<IImapClientFactory>();

        var options = Options.Create(new ImapOptions
        {
            AutoExecuteSterileUnsubscribe = true
        });

        var service = new ImapWatcherService(
            options,
            mailStripperClient,
            dispatcher,
            folderManager,
            clientFactory,
            NullLogger<ImapWatcherService>.Instance);

        await service.ProcessSingleMessageAsync(folder, uid, CancellationToken.None);

        // Verify dispatch was attempted
        await dispatcher.Received(1).DispatchAsync("https://example.com/one-click", candidateReport, Arg.Any<CancellationToken>());

        // Verify NOT moved to archive on failure
        await folderManager.DidNotReceive().MoveWithKeywordAsync(
            Arg.Any<IMailFolder>(),
            Arg.Any<UniqueId>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSingleMessageAsync_ExceedsByteCap_SkipsAnalysis()
    {
        var uid = new UniqueId(505);
        var folder = Substitute.For<IMailFolder>();
        // Create large body exceeding the 100 byte cap configured below
        folder.GetMessageAsync(uid, Arg.Any<CancellationToken>()).Returns(CreateDummyMessage(body: new string('A', 500)));

        var mailStripperClient = Substitute.For<IMailStripperClient>();
        var dispatcher = Substitute.For<ISterileUnsubscribeDispatcher>();
        var folderManager = Substitute.For<IImapFolderManager>();
        var clientFactory = Substitute.For<IImapClientFactory>();

        var options = Options.Create(new ImapOptions
        {
            MaxMessageByteSize = 100 // strict cap for test
        });

        var service = new ImapWatcherService(
            options,
            mailStripperClient,
            dispatcher,
            folderManager,
            clientFactory,
            NullLogger<ImapWatcherService>.Instance);

        await service.ProcessSingleMessageAsync(folder, uid, CancellationToken.None);

        // Verify mailStripperClient was skipped due to size cap
        await mailStripperClient.DidNotReceive().AnalyzeMessageStreamAsync(
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ReconnectionPath_CallsNextDelayAndRetries()
    {
        var client1 = Substitute.For<IImapClient>();
        var client2 = Substitute.For<IImapClient>();
        var mockFolder = Substitute.For<IMailFolder>();
        var clientFactory = Substitute.For<IImapClientFactory>();
        var backoff = Substitute.For<IBackoffPolicy>();
        var folderManager = Substitute.For<IImapFolderManager>();
        var mailStripperClient = Substitute.For<IMailStripperClient>();
        var dispatcher = Substitute.For<ISterileUnsubscribeDispatcher>();

        using var cts = new CancellationTokenSource();

        // Client 1 fails to connect
        client1.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Throws(new System.IO.IOException("Simulated connection dropped"));

        // Client 2 connects, opens folder, sweeps, and we cancel cts to stop the loop
        client2.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        client2.GetFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(mockFolder);
        mockFolder.SearchAsync(Arg.Any<MailKit.Search.SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((System.Collections.Generic.IList<UniqueId>)new System.Collections.Generic.List<UniqueId>()));
        mockFolder.OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>())
            .Returns(FolderAccess.ReadWrite)
            .AndDoes(_ => cts.Cancel());

        clientFactory.CreateClient().Returns(client1, client2);
        backoff.NextDelay().Returns(TimeSpan.FromMilliseconds(5));

        var options = Options.Create(new ImapOptions());
        var service = new TestableImapWatcherService(
            options,
            mailStripperClient,
            dispatcher,
            folderManager,
            clientFactory,
            NullLogger<ImapWatcherService>.Instance,
            backoff);

        await service.RunAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10));

        // Assert: Factory created client 1 (failed) and client 2 (recovered)
        clientFactory.Received(2).CreateClient();

        // Assert: NextDelay was called on backoff for the failure
        backoff.Received(1).NextDelay();
    }

    [Fact]
    public async Task ProcessSingleMessageAsync_AlreadyTaggedSafeUnsubscribed_SkipsAnalysisAndDispatch()
    {
        var uid = new UniqueId(999);
        var folder = Substitute.For<IMailFolder>();
        var summary = Substitute.For<IMessageSummary>();
        summary.Keywords.Returns(new System.Collections.Generic.HashSet<string> { ImapKeywords.SafeUnsubscribed });

        folder.FetchAsync(
            Arg.Any<System.Collections.Generic.IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(new System.Collections.Generic.List<IMessageSummary> { summary });

        var mailStripperClient = Substitute.For<IMailStripperClient>();
        var dispatcher = Substitute.For<ISterileUnsubscribeDispatcher>();
        var folderManager = Substitute.For<IImapFolderManager>();
        var clientFactory = Substitute.For<IImapClientFactory>();

        var options = Options.Create(new ImapOptions { AutoExecuteSterileUnsubscribe = true });
        var service = new ImapWatcherService(
            options,
            mailStripperClient,
            dispatcher,
            folderManager,
            clientFactory,
            NullLogger<ImapWatcherService>.Instance);

        await service.ProcessSingleMessageAsync(folder, uid, CancellationToken.None);

        // Verify message was never downloaded
        await folder.DidNotReceive().GetMessageAsync(uid, Arg.Any<CancellationToken>());

        // Verify analysis and dispatcher were skipped
        await mailStripperClient.DidNotReceive().AnalyzeMessageStreamAsync(
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        await dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<ForensicReportDto>(),
            Arg.Any<CancellationToken>());
    }

    private class TestableImapWatcherService : ImapWatcherService
    {
        public TestableImapWatcherService(
            IOptions<ImapOptions> options,
            IMailStripperClient mailStripperClient,
            ISterileUnsubscribeDispatcher dispatcher,
            IImapFolderManager folderManager,
            IImapClientFactory clientFactory,
            Microsoft.Extensions.Logging.ILogger<ImapWatcherService> logger,
            IBackoffPolicy? backoffPolicy = null)
            : base(options, mailStripperClient, dispatcher, folderManager, clientFactory, logger, backoffPolicy)
        {
        }

        public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
    }
}
