using MailKit;
using MailKit.Net.Imap;
using MailStripper.Imap.Models;
using MailStripper.Imap.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailStripper.Imap.Tests;

public class FolderManagerTests
{
    private readonly ImapFolderManager _manager = new(NullLogger<ImapFolderManager>.Instance);

    [Fact]
    public async Task EnsureFolderAsync_ReturnsDirectly_WhenFolderExists()
    {
        var client = Substitute.For<IImapClient>();
        var folder = Substitute.For<IMailFolder>();
        folder.FullName.Returns("INBOX");
        client.GetFolderAsync("INBOX", Arg.Any<CancellationToken>()).Returns(folder);

        var result = await _manager.EnsureFolderAsync(client, "INBOX");

        Assert.Same(folder, result);
        await client.Received(1).GetFolderAsync("INBOX", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureFolderAsync_CreatesMissingFolderHierarchy()
    {
        var client = Substitute.For<IImapClient>();
        var root = Substitute.For<IMailFolder>();
        var parentFolder = Substitute.For<IMailFolder>();
        var childFolder = Substitute.For<IMailFolder>();

        root.DirectorySeparator.Returns('/');
        parentFolder.FullName.Returns("@Safe-Unsubscribe");
        childFolder.FullName.Returns("@Safe-Unsubscribe/Archive");

        // Direct get throws FolderNotFoundException
        client.GetFolderAsync("@Safe-Unsubscribe/Archive", Arg.Any<CancellationToken>())
            .Throws(new FolderNotFoundException("@Safe-Unsubscribe/Archive"));

        // Fallback to namespace root
        client.GetFolderAsync(string.Empty, Arg.Any<CancellationToken>()).Returns(root);

        // First segment: parent missing, so created
        root.GetSubfolderAsync("@Safe-Unsubscribe", Arg.Any<CancellationToken>())
            .Throws(new FolderNotFoundException("@Safe-Unsubscribe"));
        root.CreateAsync("@Safe-Unsubscribe", isMessageFolder: true, Arg.Any<CancellationToken>())
            .Returns(parentFolder);

        // Second segment: child missing, so created
        parentFolder.GetSubfolderAsync("Archive", Arg.Any<CancellationToken>())
            .Throws(new FolderNotFoundException("Archive"));
        parentFolder.CreateAsync("Archive", isMessageFolder: true, Arg.Any<CancellationToken>())
            .Returns(childFolder);

        var result = await _manager.EnsureFolderAsync(client, "@Safe-Unsubscribe/Archive");

        Assert.Same(childFolder, result);
        await root.Received(1).CreateAsync("@Safe-Unsubscribe", isMessageFolder: true, Arg.Any<CancellationToken>());
        await parentFolder.Received(1).CreateAsync("Archive", isMessageFolder: true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionHierarchyAsync_EnsuresAllConfiguredFolders()
    {
        var client = Substitute.For<IImapClient>();
        var options = new ImapOptions
        {
            MonitoredFolder = "@Safe-Unsubscribe",
            ArchiveFolder = "@Safe-Unsubscribe/Archive",
            QuarantineFolder = "@Safe-Unsubscribe/Quarantine",
            AuditFolder = "@Safe-Unsubscribe/Audit"
        };

        var mockFolder = Substitute.For<IMailFolder>();
        client.GetFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(mockFolder);

        await _manager.ProvisionHierarchyAsync(client, options);

        await client.Received(1).GetFolderAsync("@Safe-Unsubscribe", Arg.Any<CancellationToken>());
        await client.Received(1).GetFolderAsync("@Safe-Unsubscribe/Archive", Arg.Any<CancellationToken>());
        await client.Received(1).GetFolderAsync("@Safe-Unsubscribe/Quarantine", Arg.Any<CancellationToken>());
        await client.Received(1).GetFolderAsync("@Safe-Unsubscribe/Audit", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryAddCustomKeywordAsync_AppliesFlag_WhenServerSupportsKeywords()
    {
        var folder = Substitute.For<IMailFolder>();
        var uid = new UniqueId(42);

        var success = await _manager.TryAddCustomKeywordAsync(folder, uid, ImapKeywords.SuspiciousTrap);

        Assert.True(success);
        await folder.Received(1).StoreAsync(
            uid,
            Arg.Any<IStoreFlagsRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryAddCustomKeywordAsync_CatchesExceptionAndReturnsFalse()
    {
        var folder = Substitute.For<IMailFolder>();
        var uid = new UniqueId(42);

        folder.StoreAsync(
            uid,
            Arg.Any<IStoreFlagsRequest>(),
            Arg.Any<CancellationToken>())
            .Throws(new NotSupportedException("Server lacks user flag support."));

        var success = await _manager.TryAddCustomKeywordAsync(folder, uid, ImapKeywords.SuspiciousTrap);

        Assert.False(success);
    }

    [Fact]
    public async Task MoveWithKeywordAsync_TagsSourceAndMovesMessage()
    {
        var sourceFolder = Substitute.For<IMailFolder>();
        var destFolder = Substitute.For<IMailFolder>();
        var uid = new UniqueId(100);

        sourceFolder.MoveToAsync(uid, destFolder, Arg.Any<CancellationToken>())
            .Returns(new UniqueId(200));
        destFolder.IsOpen.Returns(false);

        await _manager.MoveWithKeywordAsync(sourceFolder, uid, destFolder, ImapKeywords.SafeUnsubscribed);

        // Verify StoreAsync was called on source folder (via AddFlagsAsync extension)
        await sourceFolder.Received(1).StoreAsync(
            uid,
            Arg.Any<IStoreFlagsRequest>(),
            Arg.Any<CancellationToken>());

        // Verify move called
        await sourceFolder.Received(1).MoveToAsync(uid, destFolder, Arg.Any<CancellationToken>());
    }
}
