using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class PageRequestTests
{
    [Fact]
    public void Limit_falls_back_to_the_lists_default_and_is_clamped()
    {
        Assert.Equal(50, new PageRequest().LimitOr(50));
        Assert.Equal(7, new PageRequest { Limit = 7 }.LimitOr(50));
        Assert.Equal(Paging.MaxPageSize, new PageRequest { Limit = int.MaxValue }.LimitOr(50));
        Assert.Equal(1, new PageRequest { Limit = -3 }.LimitOr(50));
    }

    [Fact]
    public void The_old_page_size_parameters_still_count_but_limit_wins()
    {
        Assert.Equal(9, new PageRequest { Take = 9 }.LimitOr(50));
        Assert.Equal(8, new PageRequest { Top = 8 }.LimitOr(50));
        Assert.Equal(4, new PageRequest { Limit = 4, Take = 9, Top = 8 }.LimitOr(50));
    }

    [Fact]
    public void A_keyset_cursor_is_the_key_to_read_after()
    {
        Assert.Null(new PageRequest().AfterKey().Value);
        Assert.Equal(42, new PageRequest { Cursor = "42" }.AfterKey().Value);
        Assert.Equal(17, new PageRequest { FromEventId = 17 }.AfterKey().Value);
        Assert.Equal(42, new PageRequest { Cursor = "42", FromEventId = 17 }.AfterKey().Value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData("99999999999999999999")]
    public void A_cursor_no_page_handed_out_is_a_validation_error(string cursor)
    {
        var key = new PageRequest { Cursor = cursor }.AfterKey();
        var offset = new PageRequest { Cursor = cursor }.Offset();

        Assert.Equal("PAGE_CURSOR_INVALID", key.Error.Code);
        Assert.Equal(ErrorType.Validation, key.Error.Type);
        Assert.Equal("PAGE_CURSOR_INVALID", offset.Error.Code);
    }

    [Fact]
    public void An_offset_list_reads_its_cursor_then_the_old_skip()
    {
        Assert.Equal(0, new PageRequest().Offset().Value);
        Assert.Equal(20, new PageRequest { Cursor = "20" }.Offset().Value);
        Assert.Equal(10, new PageRequest { Skip = 10 }.Offset().Value);
        Assert.Equal(0, new PageRequest { Skip = -5 }.Offset().Value);
        Assert.Equal(20, new PageRequest { Cursor = "20", Skip = 10 }.Offset().Value);
    }

    [Fact]
    public void An_offset_page_hands_out_the_next_offset_while_rows_remain()
    {
        var first = PageRequest.OffsetPage(new PagedList<string>(["a", "b"], 5), offset: 0);
        var middle = PageRequest.OffsetPage(new PagedList<string>(["c", "d"], 5), offset: 2);
        var last = PageRequest.OffsetPage(new PagedList<string>(["e"], 5), offset: 4);

        Assert.Equal(["a", "b"], first.Items);
        Assert.Equal(5, first.TotalCount);
        Assert.Equal("2", first.NextCursor);
        Assert.Equal("4", middle.NextCursor);
        Assert.Null(last.NextCursor);
    }

    [Fact]
    public void An_offset_page_past_the_end_is_empty_and_last()
    {
        var page = PageRequest.OffsetPage(new PagedList<string>([], 5), offset: 40);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void A_keyset_cursor_is_written_without_culture()
    {
        Assert.Equal("1234567", PageRequest.KeyCursor(1234567));
        Assert.Null(PageRequest.KeyCursor(null));
    }
}

public sealed class TimeRangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_missing_end_is_now_and_a_missing_start_is_the_default_span_before_it()
    {
        var range = TimeRange.Resolve(null, null, Now, TimeSpan.FromDays(30), TimeSpan.FromDays(400));

        Assert.Equal(Now.UtcDateTime, range.Value.ToUtc);
        Assert.Equal(Now.UtcDateTime.AddDays(-30), range.Value.FromUtc);
    }

    [Fact]
    public void Offsets_are_read_as_the_instant_they_name()
    {
        var from = new DateTimeOffset(2026, 10, 1, 5, 30, 0, TimeSpan.FromHours(5.5));

        var range = TimeRange.Resolve(from, null, Now, TimeSpan.FromDays(1), TimeSpan.FromDays(400));

        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), range.Value.FromUtc);
        Assert.Equal(DateTimeKind.Utc, range.Value.FromUtc.Kind);
    }

    [Fact]
    public void An_empty_or_backwards_range_is_invalid()
    {
        Assert.Equal("TIME_RANGE_INVALID", TimeRange.Resolve(Now, Now, Now, TimeSpan.FromDays(1), TimeSpan.FromDays(400)).Error.Code);
        Assert.Equal("TIME_RANGE_INVALID", TimeRange.Resolve(Now, Now.AddDays(-1), Now, TimeSpan.FromDays(1), TimeSpan.FromDays(400)).Error.Code);
        Assert.Equal("TIME_RANGE_INVALID", TimeRange.Resolve(new DateTimeOffset(1960, 1, 1, 0, 0, 0, TimeSpan.Zero), null, Now, TimeSpan.FromDays(1), TimeSpan.FromDays(400000)).Error.Code);
    }

    [Fact]
    public void A_range_longer_than_the_cap_is_rejected()
    {
        var range = TimeRange.Resolve(Now.AddDays(-401), Now, Now, TimeSpan.FromDays(1), TimeSpan.FromDays(400));

        Assert.Equal("TIME_RANGE_TOO_LONG", range.Error.Code);
    }
}
