using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class EventLogPagingTests
{
    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 1)]
    [InlineData(50, 50)]
    [InlineData(200, 200)]
    [InlineData(int.MaxValue, Paging.MaxPageSize)]
    public void Page_sizes_are_clamped(int asked, int used) => Assert.Equal(used, Paging.Take(asked));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(40, 40)]
    public void A_negative_offset_is_zero(int asked, int used) => Assert.Equal(used, Paging.Skip(asked));

    [Fact]
    public void A_full_probe_hands_back_the_last_shown_row_as_the_cursor()
    {
        var page = EventLogService.ToPage([Row(9), Row(8), Row(7)], take: 2);

        Assert.Equal(["e9", "e8"], page.Items.Select(i => i.EventName));
        Assert.Equal(8, page.NextCursor);
    }

    [Fact]
    public void The_last_page_has_no_cursor()
    {
        var page = EventLogService.ToPage([Row(2), Row(1)], take: 2);

        Assert.Equal(2, page.Items.Count());
        Assert.Null(page.NextCursor);
    }

    private static EventLogRow Row(long id) =>
        new(id, new EventLogResponse($"e{id}", "{}", EventCriticality.Info, null, null, null, DateTime.UtcNow));
}
