using System;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// The catalog refused the request because the caller is sending too many.
/// </summary>
/// <remarks>
/// Deliberately an exception rather than a null body: null means "not found"
/// and gets cached as such. A rate-limited lookup must not be remembered as a
/// miss, or every item touched during the burst stays unmatched for a day.
/// </remarks>
public class CatalogRateLimitedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogRateLimitedException"/> class.
    /// </summary>
    public CatalogRateLimitedException()
        : base("Apple Music rate limited the request.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogRateLimitedException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    public CatalogRateLimitedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogRateLimitedException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public CatalogRateLimitedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
