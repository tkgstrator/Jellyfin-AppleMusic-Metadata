using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata;

internal static class EmbeddedTagParser
{
    internal static EmbeddedMetadata Parse(string json, EmbeddedTagOptions options)
    {
        using var document = JsonDocument.Parse(json);
        var sources = new List<JsonElement>();
        var root = document.RootElement;
        if (root.TryGetProperty("format", out var format) && format.TryGetProperty("tags", out var formatTags))
        {
            sources.Add(formatTags);
        }

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio" && stream.TryGetProperty("tags", out var tags))
                {
                    sources.Add(tags);
                }
            }
        }

        var conflicts = new List<string>();
        IReadOnlyList<string> Values(params string[] aliases) => sources.SelectMany(source => source.EnumerateObject())
            .Where(property => aliases.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            .SelectMany(property => property.Value.ValueKind == JsonValueKind.Array ? property.Value.EnumerateArray().Select(element => element.GetString() ?? string.Empty) : [property.Value.GetString() ?? string.Empty])
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();

        string? Scalar(string field, params string[] aliases)
        {
            var values = Values(aliases).DistinctBy(value => value.Normalize(NormalizationForm.FormC)).ToArray();
            if (values.Length <= 1)
            {
                return values.FirstOrDefault();
            }

            conflicts.Add(field);
            return null;
        }

        int? Number(string field, Func<string, int?> parse, params string[] aliases)
        {
            var values = Values(aliases).Select(parse).Where(value => value.HasValue).Distinct().ToArray();
            if (values.Length <= 1)
            {
                return values.FirstOrDefault();
            }

            conflicts.Add(field);
            return null;
        }

        IReadOnlyList<string> List(string field, string[] aliases, string? preferred = null)
        {
            if (options.PreferNonstandardArtists && preferred is not null && Values(preferred).Count > 0)
            {
                aliases = [preferred];
            }

            // Compare whole lists between tag locations, not individual artists.
            var lists = sources.SelectMany(source => source.EnumerateObject()
                    .Where(property => aliases.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.SelectMany(property => property.Value.ValueKind == JsonValueKind.Array ? property.Value.EnumerateArray().Select(element => element.GetString() ?? string.Empty) : [property.Value.GetString() ?? string.Empty])
                        .SelectMany(value => Split(value, options))
                        .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
                        .DistinctBy(value => value.Normalize(NormalizationForm.FormC), StringComparer.OrdinalIgnoreCase).ToArray()))
                .Where(list => list.Length > 0).ToArray();
            if (lists.Length == 0)
            {
                return [];
            }

            if (lists.Skip(1).Any(list => !EmbeddedMetadata.SameList(lists[0], list)))
            {
                conflicts.Add(field);
                return [];
            }

            return lists[0];
        }

        return new EmbeddedMetadata
        {
            Artists = List("Artists", ["artist"], "artists"),
            AlbumArtists = List("AlbumArtists", ["albumartist", "album artist", "album_artist"], "albumartists"),
            Album = Scalar("Album", "album"),
            Track = Number("Track", ParseNumber, "track", "tracknumber", "trck"),
            Disc = Number("Disc", ParseNumber, "disc", "discnumber", "disk", "tpos"),
            Year = Number("Year", ParseYear, "date", "year"),
            Genres = List("Genres", ["genre"]),
            Conflicts = conflicts
        };
    }

    private static int? ParseNumber(string value)
    {
        var parts = value.Split('/');
        if (parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            return null;
        }

        if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) || total <= 0 || number > total))
        {
            return null;
        }

        return number;
    }

    private static int? ParseYear(string value)
    {
        if (value.Length == 4 && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year > 0)
        {
            return year;
        }

        return DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd", "yyyy-MM", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.Year : null;
    }

    private static IEnumerable<string> Split(string value, EmbeddedTagOptions options)
    {
        // NUL is an actual multi-value boundary. Punctuation is only a boundary
        // when the library explicitly enables it; AC/DC must otherwise survive.
        foreach (var segment in value.Split('\0'))
        {
            var buffer = new StringBuilder();
            for (var index = 0; index < segment.Length;)
            {
                var protectedName = options.Whitelist.Where(name => name.Length > 0)
                    .OrderByDescending(name => name.Length)
                    .FirstOrDefault(name => segment.AsSpan(index).StartsWith(name, StringComparison.OrdinalIgnoreCase));
                if (protectedName is not null)
                {
                    buffer.Append(segment, index, protectedName.Length);
                    index += protectedName.Length;
                    continue;
                }

                var delimiter = options.Delimiters.Where(item => item.Length > 0).OrderByDescending(item => item.Length)
                    .FirstOrDefault(item => segment.AsSpan(index).StartsWith(item, StringComparison.Ordinal));
                if (delimiter is not null)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                    index += delimiter.Length;
                }
                else
                {
                    buffer.Append(segment[index++]);
                }
            }

            yield return buffer.ToString();
        }
    }
}
