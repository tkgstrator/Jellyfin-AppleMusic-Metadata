using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.AppleMusic.Catalog.Models;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Coverage;

/// <summary>
/// Decides whether a library artist name identifies one of the catalog's
/// search results.
/// </summary>
public static partial class ArtistMatcher
{
    /// <summary>
    /// Classifies a name against the results of searching for it.
    /// </summary>
    /// <param name="name">Artist name as the library has it.</param>
    /// <param name="results">Search results, best first.</param>
    /// <returns>The entry to record.</returns>
    public static ArtistCoverageEntry Classify(string name, IReadOnlyList<CatalogItem<ArtistAttributes>> results)
    {
        if (results.Count == 0)
        {
            return new ArtistCoverageEntry { Name = name, Outcome = ArtistMatchOutcome.NotFound };
        }

        var trimmed = name.Trim();
        var exact = results.FirstOrDefault(result => string.Equals(result.Attributes.Name?.Trim(), trimmed, StringComparison.Ordinal));
        if (exact is not null)
        {
            return Entry(name, ArtistMatchOutcome.Exact, exact);
        }

        var relaxedName = Relax(name);
        var relaxed = results.FirstOrDefault(result => string.Equals(Relax(result.Attributes.Name), relaxedName, StringComparison.OrdinalIgnoreCase));
        return relaxed is not null
            ? Entry(name, ArtistMatchOutcome.Relaxed, relaxed)
            : Entry(name, ArtistMatchOutcome.Unmatched, results[0]);
    }

    /// <summary>
    /// Collapses the differences the relaxed comparison ignores: surrounding
    /// and repeated whitespace. Case is handled by the comparer.
    /// </summary>
    /// <param name="name">Name to normalise.</param>
    /// <returns>The normalised name.</returns>
    internal static string Relax(string? name)
        => name is null ? string.Empty : Whitespace().Replace(name.Trim(), " ");

    private static ArtistCoverageEntry Entry(string name, ArtistMatchOutcome outcome, CatalogItem<ArtistAttributes> candidate)
        => new()
        {
            Name = name,
            Outcome = outcome,
            Candidate = candidate.Attributes.Name,
            CandidateId = candidate.Id,
            Storefront = candidate.Storefront,
        };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
