using MailStripper.Imap.Models;
using MailStripper.Imap.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ImapOptions>(
    builder.Configuration.GetSection(ImapOptions.SectionName));

builder.Services.AddHttpClient<IMailStripperClient, MailStripperClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<ImapOptions>>().Value;
    client.BaseAddress = new Uri(opts.MailStripperUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<ISterileUnsubscribeDispatcher, SterileUnsubscribeDispatcher>();
builder.Services.AddHostedService<ImapWatcherService>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=================================================");
logger.LogInformation("MailStripper IMAP Protocol Watcher (v0.1.0)");
logger.LogInformation("100% Volatile RAM Ingestion · Zero Disk Writes");
logger.LogInformation("Pairs with local mailStripper (:5001)");
logger.LogInformation("=================================================");

await host.RunAsync();
