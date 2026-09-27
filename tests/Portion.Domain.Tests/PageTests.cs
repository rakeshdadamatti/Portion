using Portion.Domain.Common;

namespace Portion.Domain.Tests;

/// <summary>Covers the pagination primitives, which every list endpoint depends on.</summary>
public class PageTests
{
    [Fact]
    public void TryValidate_AcceptsDefaults_WhenValuesAreAbsent()
    {
        var valid = Page.TryValidate(null, null, out var errors);

        Assert.True(valid);
        Assert.Empty(errors);
    }

    [Fact]
    public void TryValidate_RejectsZeroPageNumber()
    {
        var valid = Page.TryValidate(0, null, out var errors);

        Assert.False(valid);
        Assert.True(errors.ContainsKey("page"));
    }

    [Fact]
    public void TryValidate_RejectsPageSizeAboveMaximum()
    {
        var valid = Page.TryValidate(1, Page.MaxPageSize + 1, out var errors);

        Assert.False(valid);
        Assert.True(errors.ContainsKey("pageSize"));
    }

    [Fact]
    public void TryValidate_AcceptsMaximumPageSize()
    {
        Assert.True(Page.TryValidate(1, Page.MaxPageSize, out _));
    }

    [Fact]
    public void Create_ClampsOutOfRangeValues_InsteadOfThrowing()
    {
        var page = Page.Create(number: -5, size: 10_000);

        Assert.Equal(1, page.Number);
        Assert.Equal(Page.MaxPageSize, page.Size);
    }

    [Fact]
    public void Skip_IsZeroForFirstPage()
    {
        Assert.Equal(0, Page.Create(1, 25).Skip);
    }

    [Fact]
    public void Skip_IsOffsetByPriorPages()
    {
        Assert.Equal(50, Page.Create(3, 25).Skip);
    }

    [Fact]
    public void CreatePage_RoundsTotalPagesUpward_SoTheLastItemIsReachable()
    {
        var page = PagedResult<string>.Create(["a", "b", "c"], Page.Create(1, 2), totalCount: 3);

        Assert.Equal(2, page.TotalPages);
    }

    [Fact]
    public void CreatePage_ReportsZeroTotalPages_WhenNothingMatched()
    {
        var page = PagedResult<string>.Create([], Page.Create(1, 25), totalCount: 0);

        Assert.Equal(0, page.TotalPages);
        Assert.Empty(page.Items);
    }
}
