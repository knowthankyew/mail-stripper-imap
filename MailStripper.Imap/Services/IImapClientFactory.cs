using MailKit.Net.Imap;

namespace MailStripper.Imap.Services;

public interface IImapClientFactory
{
    IImapClient CreateClient();
}

public class DefaultImapClientFactory : IImapClientFactory
{
    public IImapClient CreateClient() => new ImapClient();
}
