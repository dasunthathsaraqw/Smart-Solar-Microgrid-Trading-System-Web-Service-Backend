/**
 * File: PagedResult.cs
 * Purpose: Generic paged response wrapper used by search/list endpoints that support pagination.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class PagedResult<T>
{
    /// <summary>Items in the requested page.</summary>
    public List<T> Items { get; set; } = new();

    /// <summary>Total matching items across all pages.</summary>
    public int TotalCount { get; set; }

    /// <summary>Current one-based page number.</summary>
    public int Page { get; set; }

    /// <summary>Maximum number of items requested per page.</summary>
    public int PageSize { get; set; }

    /// <summary>Total number of pages, or zero when there are no matches.</summary>
    public int TotalPages { get; set; }

    /// <summary>True when a later page is available.</summary>
    public bool HasNextPage { get; set; }

    /// <summary>True when an earlier page is available.</summary>
    public bool HasPreviousPage { get; set; }
}
