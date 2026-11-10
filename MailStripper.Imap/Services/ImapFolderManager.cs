using MailKit;
using MailKit.Net.Imap;
using MailStripper.Imap.Models;
using Microsoft.Extensions.Logging;

namespace MailStripper.Imap.Services;

public interface IImapFolderManager
{
    Task ProvisionHierarchyAsync(IImapClient client, ImapOptions options, CancellationToken ct = default);
    Task<IMailFolder> EnsureFolderAsync(IImapClient client, string folderPath, CancellationToken ct = default);
    Task<IMailFolder?> ResolveFolderAsync(IMailFolder currentFolder, string targetPath, CancellationToken ct = default);
    Task<bool> TryAddCustomKeywordAsync(IMailFolder folder, UniqueId uid, string keyword, CancellationToken ct = default);
    Task MoveWithKeywordAsync(IMailFolder sourceFolder, UniqueId uid, IMailFolder destinationFolder, string keyword, CancellationToken ct = default);
}

public class ImapFolderManager : IImapFolderManager
{
    private readonly ILogger<ImapFolderManager> _logger;

    public ImapFolderManager(ILogger<ImapFolderManager> logger)
    {
        _logger = logger;
    }

    public async Task ProvisionHierarchyAsync(IImapClient client, ImapOptions options, CancellationToken ct = default)
    {
        var foldersToEnsure = new[]
        {
            options.MonitoredFolder,
            options.ArchiveFolder,
            options.QuarantineFolder,
            options.AuditFolder
        };

        foreach (var path in foldersToEnsure)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            _logger.LogInformation("Verifying mailbox folder: '{Path}'...", path);
            await EnsureFolderAsync(client, path, ct);
        }
    }

    public async Task<IMailFolder> EnsureFolderAsync(IImapClient client, string folderPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path cannot be empty.", nameof(folderPath));
        }

        try
        {
            return await client.GetFolderAsync(folderPath, ct);
        }
        catch (FolderNotFoundException)
        {
            _logger.LogDebug("Folder '{Path}' not found directly. Resolving from personal namespace root...", folderPath);
        }

        var root = await client.GetFolderAsync(string.Empty, ct);
        return await EnsureFolderFromRootAsync(root, folderPath, ct);
    }

    public async Task<IMailFolder?> ResolveFolderAsync(IMailFolder currentFolder, string targetPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        IMailFolder root = currentFolder;
        while (root.ParentFolder != null)
        {
            root = root.ParentFolder;
        }

        return await EnsureFolderFromRootAsync(root, targetPath, ct);
    }

    private async Task<IMailFolder> EnsureFolderFromRootAsync(IMailFolder root, string folderPath, CancellationToken ct)
    {
        var sep = root.DirectorySeparator;
        // '/' is the portable cross-platform configuration separator; sep is the server-specific namespace delimiter
        var parts = folderPath.Split(new[] { '/', sep }, StringSplitOptions.RemoveEmptyEntries);

        IMailFolder current = root;
        foreach (var part in parts)
        {
            try
            {
                current = await current.GetSubfolderAsync(part, ct);
            }
            catch (FolderNotFoundException)
            {
                _logger.LogInformation("Creating missing mailbox folder '{Part}' under '{Parent}'...",
                    part, current.FullName);
                var created = await current.CreateAsync(part, isMessageFolder: true, ct);
                current = created ?? throw new InvalidOperationException($"Failed to create subfolder '{part}' under '{current.FullName}'.");
            }
        }

        return current;
    }

    public async Task<bool> TryAddCustomKeywordAsync(IMailFolder folder, UniqueId uid, string keyword, CancellationToken ct = default)
    {
        try
        {
            await folder.AddFlagsAsync(uid, MessageFlags.None, new HashSet<string> { keyword }, silent: false, ct);
            _logger.LogDebug("Tagged message {Uid} in '{Folder}' with keyword {Keyword}.", uid, folder.FullName, keyword);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not apply custom keyword {Keyword} to message {Uid} in '{Folder}'. Server may not support user-defined flags.",
                keyword, uid, folder.FullName);
            return false;
        }
    }

    public async Task MoveWithKeywordAsync(IMailFolder sourceFolder, UniqueId uid, IMailFolder destinationFolder, string keyword, CancellationToken ct = default)
    {
        // Tag keyword in source folder before moving (so flags are copied by server during MOVE/COPY)
        await TryAddCustomKeywordAsync(sourceFolder, uid, keyword, ct);

        // Move to destination folder
        var newUid = await sourceFolder.MoveToAsync(uid, destinationFolder, ct);

        // If destination folder happens to be open and server returned the new UID, verify tag
        if (newUid.HasValue && destinationFolder.IsOpen)
        {
            await TryAddCustomKeywordAsync(destinationFolder, newUid.Value, keyword, ct);
        }
    }
}
