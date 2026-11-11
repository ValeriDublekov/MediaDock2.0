using MediaDock.Api.BackgroundJobs;

namespace MediaDock.IntegrationTests;

public sealed class BackgroundJobSchedulerTests
{
    [Theory]
    [InlineData("2026-01-07T00:59:59Z", "2025-12-31T01:00:00Z")]
    [InlineData("2026-01-07T01:00:00Z", "2026-01-07T01:00:00Z")]
    [InlineData("2026-07-08T00:00:00Z", "2026-07-08T00:00:00Z")]
    [InlineData("2026-11-11T12:00:00Z", "2026-11-11T01:00:00Z")]
    public void ImdbRatingRefreshUsesLatestWednesdayThreeAmSofiaSlot(string now, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), BackgroundJobScheduler.GetLatestImdbRatingSlot(DateTimeOffset.Parse(now)));
    }

    [Fact]
    public void CalculatesTheScheduledSlotsAcrossTheSpringDstTransition()
    {
        var after = new DateTimeOffset(2026, 3, 28, 16, 0, 0, TimeSpan.Zero);
        var through = new DateTimeOffset(2026, 3, 29, 15, 0, 0, TimeSpan.Zero);

        var slots = BackgroundJobScheduler.GetDueSlots(after, through);

        Assert.Equal(
            [
                new DateTimeOffset(2026, 3, 29, 4, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 29, 15, 0, 0, TimeSpan.Zero)
            ],
            slots);
    }

    [Fact]
    public void CalculatesTheScheduledSlotsAcrossTheAutumnDstTransition()
    {
        var after = new DateTimeOffset(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);
        var through = new DateTimeOffset(2026, 10, 25, 16, 0, 0, TimeSpan.Zero);

        var slots = BackgroundJobScheduler.GetDueSlots(after, through);

        Assert.Equal(
            [
                new DateTimeOffset(2026, 10, 25, 5, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 25, 16, 0, 0, TimeSpan.Zero)
            ],
            slots);
    }

    [Fact]
    public void UsesAnExclusiveCheckpointAndInclusiveCurrentTimeForCoalescedCatchUp()
    {
        var checkpoint = new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);
        var through = new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.Zero);

        var slots = BackgroundJobScheduler.GetDueSlots(checkpoint, through);

        Assert.Equal(
            [
                new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.Zero)
            ],
            slots);
    }
}