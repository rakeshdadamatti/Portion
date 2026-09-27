namespace Portion.Domain.Common;

/// <summary>A validated, 1-based pagination request.</summary>
public sealed record Page
{
    /// <summary>Largest page size a caller may request.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Page size used when the caller does not supply one.</summary>
    public const int DefaultPageSize = 25;

    private Page(int number, int size)
    {
        Number = number;
        Size = size;
    }

    /// <summary>1-based page number.</summary>
    public int Number { get; }

    /// <summary>Number of items per page, between 1 and <see cref="MaxPageSize" />.</summary>
    public int Size { get; }

    /// <summary>Number of items to skip.</summary>
    public int Skip => (Number - 1) * Size;

    /// <summary>A default 1/25 page.</summary>
    public static Page Default => new(1, DefaultPageSize);

    /// <summary>Creates a page, clamping the size into range.</summary>
    public static Page Create(int number, int size)
    {
        var clampedNumber = number < 1 ? 1 : number;
        var clampedSize = size switch
        {
            < 1 => 1,
            > MaxPageSize => MaxPageSize,
            _ => size
        };

        return new Page(clampedNumber, clampedSize);
    }

    /// <summary>Validates the raw query-string values without clamping.</summary>
    public static bool TryValidate(int? number, int? size, out IReadOnlyDictionary<string, string[]> errors)
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (number is < 1)
        {
            map["page"] = ["Page must be greater than or equal to 1."];
        }

        if (size is < 1 || size > MaxPageSize)
        {
            map["pageSize"] = [$"PageSize must be between 1 and {MaxPageSize}."];
        }

        errors = map;
        return map.Count == 0;
    }
}

/// <summary>A page of results plus the metadata needed to render pagination controls.</summary>
public sealed record PagedResult<T>
{
    /// <summary>An empty page.</summary>
    public static PagedResult<T> Empty(Page page) =>
        new(Array.Empty<T>(), page.Number, page.Size, 0, 0);

    private PagedResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount, int totalPages)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = totalPages;
    }

    /// <summary>Items on this page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>1-based page number.</summary>
    public int Page { get; }

    /// <summary>Requested page size.</summary>
    public int PageSize { get; }

    /// <summary>Total number of matching items across all pages.</summary>
    public int TotalCount { get; }

    /// <summary>Total number of pages, at least 1 when there is at least one item.</summary>
    public int TotalPages { get; }

    /// <summary>Creates a page, deriving <see cref="TotalPages" /> from <paramref name="totalCount" />.</summary>
    public static PagedResult<T> Create(IReadOnlyList<T> items, Page page, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(page);

        var totalPages = totalCount <= 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)page.Size);

        return new PagedResult<T>(items, page.Number, page.Size, totalCount, totalPages);
    }
}
