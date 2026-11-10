using MailStripper.Imap.Services;
using Xunit;

namespace MailStripper.Imap.Tests;

public class ImapHealthTrackerTests
{
    [Fact]
    public void InitialState_IsStarting_AndDisconnected()
    {
        var tracker = new ImapHealthTracker();

        Assert.False(tracker.IsConnected);
        Assert.False(tracker.IsHealthy);
        Assert.Equal("Starting", tracker.Status);
        Assert.Null(tracker.LastConnectedUtc);
        Assert.Null(tracker.LastDisconnectedUtc);
        Assert.Null(tracker.LastMessageProcessedUtc);
        Assert.Equal(0, tracker.MessagesProcessedCount);
        Assert.Null(tracker.LastError);

        var report = tracker.GetReport();
        Assert.Equal("Starting", report.Status);
        Assert.False(report.Connected);
        Assert.False(report.Healthy);
        Assert.True(report.UptimeSeconds >= 0);
    }

    [Fact]
    public void ReportConnected_TransitionsToHealthy()
    {
        var tracker = new ImapHealthTracker();
        tracker.ReportConnected();

        Assert.True(tracker.IsConnected);
        Assert.True(tracker.IsHealthy);
        Assert.Equal("Healthy", tracker.Status);
        Assert.NotNull(tracker.LastConnectedUtc);
        Assert.Null(tracker.LastError);

        var report = tracker.GetReport();
        Assert.Equal("Healthy", report.Status);
        Assert.True(report.Connected);
        Assert.True(report.Healthy);
    }

    [Fact]
    public void ReportDisconnected_BeforeFirstConnection_TransitionsToUnhealthy()
    {
        var tracker = new ImapHealthTracker();
        Assert.Equal("Starting", tracker.Status);

        tracker.ReportDisconnected("Connection error (SocketException)");

        Assert.False(tracker.IsConnected);
        Assert.False(tracker.IsHealthy);
        Assert.Equal("Unhealthy", tracker.Status);
        Assert.Null(tracker.LastConnectedUtc);
        Assert.NotNull(tracker.LastDisconnectedUtc);
        Assert.Equal("Connection error (SocketException)", tracker.LastError);

        var report = tracker.GetReport();
        Assert.Equal("Unhealthy", report.Status);
        Assert.False(report.Connected);
        Assert.False(report.Healthy);
    }

    [Fact]
    public void ReportDisconnected_TransitionsToUnhealthy_AndPreservesError()
    {
        var tracker = new ImapHealthTracker();
        tracker.ReportConnected();

        tracker.ReportDisconnected("Socket connection reset by peer");

        Assert.False(tracker.IsConnected);
        Assert.False(tracker.IsHealthy);
        Assert.Equal("Unhealthy", tracker.Status);
        Assert.NotNull(tracker.LastDisconnectedUtc);
        Assert.Equal("Socket connection reset by peer", tracker.LastError);

        var report = tracker.GetReport();
        Assert.Equal("Unhealthy", report.Status);
        Assert.False(report.Connected);
        Assert.False(report.Healthy);
        Assert.Equal("Socket connection reset by peer", report.LastError);
    }

    [Fact]
    public void ReportMessageProcessed_IncrementsCountAndUpdatesTimestamp()
    {
        var tracker = new ImapHealthTracker();
        tracker.ReportConnected();

        Assert.Equal(0, tracker.MessagesProcessedCount);
        Assert.Null(tracker.LastMessageProcessedUtc);

        tracker.ReportMessageProcessed();
        Assert.Equal(1, tracker.MessagesProcessedCount);
        Assert.NotNull(tracker.LastMessageProcessedUtc);

        tracker.ReportMessageProcessed();
        Assert.Equal(2, tracker.MessagesProcessedCount);
    }
}
