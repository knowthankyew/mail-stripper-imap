using MailStripper.Imap.Models;
using MailStripper.Imap.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<ImapOptions>()
    .Bind(builder.Configuration.GetSection(ImapOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<IMailStripperClient, MailStripperClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<ImapOptions>>().Value;
    client.BaseAddress = new Uri(opts.MailStripperUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<ISterileUnsubscribeDispatcher>(sp => new SterileUnsubscribeDispatcher());
builder.Services.AddSingleton<IImapClientFactory, DefaultImapClientFactory>();
builder.Services.AddSingleton<IImapFolderManager, ImapFolderManager>();
builder.Services.AddTransient<IBackoffPolicy>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<ImapOptions>>().Value;
    return new ExponentialBackoffPolicy(
        TimeSpan.FromSeconds(opts.ReconnectInitialDelaySeconds),
        TimeSpan.FromSeconds(opts.ReconnectMaxDelaySeconds));
});
builder.Services.AddHostedService<ImapWatcherService>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=================================================");
logger.LogInformation("MailStripper IMAP Protocol Watcher (v0.2.0)");
logger.LogInformation("100% Volatile RAM Ingestion · Zero Disk Writes");
logger.LogInformation("Folder Quarantine · Custom IMAP Keywords · Auto-Recovery");
logger.LogInformation("Pairs with local mailStripper (:5001)");
logger.LogInformation("=================================================");

await host.RunAsync();
