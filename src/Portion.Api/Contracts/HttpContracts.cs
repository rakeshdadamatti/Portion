using Portion.Application.Contracts;
using Portion.Domain.Common;

namespace Portion.Api.Contracts;

/// <summary>Translation between HTTP request shapes and the application layer's query/command types.</summary>
/// <remarks>
/// Bound here rather than inside the endpoints or the feature slices so the wire contract can evolve
/// without either the routing code or the handlers noticing: the handlers only ever see their own
/// command and query records, and the endpoints only ever see HTTP primitives.
/// </remarks>
public static class HttpContractMapping
{
    /// <summary>Projects a page of resume rows onto the list response shape.</summary>
    public static PagedResponse<ResumeListItem> ToHttpResponse(this PagedResult<ResumeListItem> page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new PagedResponse<ResumeListItem>(
            page.Items,
            page.Page,
            page.PageSize,
            page.TotalCount,
            page.TotalPages);
    }
}
