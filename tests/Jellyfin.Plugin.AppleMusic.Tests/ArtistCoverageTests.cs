using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Coverage;
using Jellyfin.Plugin.AppleMusic.Catalog.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class ArtistCoverageTests
{
    [Fact]
    public void Classify_ExactMatchWinsEvenWhenNotRankedFirst()
    {
        var entry = ArtistMatcher.Classify("Aimer", Artists(("1", "Aimer & Friends"), ("2", "Aimer")));

        Assert.Equal(ArtistMatchOutcome.Exact, entry.Outcome);
        Assert.Equal("2", entry.CandidateId);
        Assert.Equal("Aimer", entry.Candidate);
        Assert.Equal("jp", entry.Storefront);
    }

    [Fact]
    public void Classify_IsCaseSensitiveForExact()
    {
        var entry = ArtistMatcher.Classify("yoasobi", Artists(("1", "YOASOBI")));

        Assert.Equal(ArtistMatchOutcome.Relaxed, entry.Outcome);
        Assert.Equal("YOASOBI", entry.Candidate);
    }

    [Fact]
    public void Classify_IgnoresSurroundingAndRepeatedWhitespaceForRelaxed()
    {
        var entry = ArtistMatcher.Classify("  King  Gnu ", Artists(("1", "King Gnu")));

        Assert.Equal(ArtistMatchOutcome.Relaxed, entry.Outcome);
    }

    [Fact]
    public void Classify_TrimsBeforeExactComparison()
    {
        var entry = ArtistMatcher.Classify("King Gnu ", Artists(("1", "King Gnu")));

        Assert.Equal(ArtistMatchOutcome.Exact, entry.Outcome);
    }

    [Fact]
    public void Classify_ReportsTheFirstResultWhenNothingMatches()
    {
        var entry = ArtistMatcher.Classify("DECO*27 feat. 初音ミク", Artists(("1", "DECO*27"), ("2", "初音ミク")));

        Assert.Equal(ArtistMatchOutcome.Unmatched, entry.Outcome);
        Assert.Equal("DECO*27", entry.Candidate);
        Assert.Equal("1", entry.CandidateId);
    }

    [Fact]
    public void Classify_ReportsNotFoundWithoutResults()
    {
        var entry = ArtistMatcher.Classify("Nobody", Artists());

        Assert.Equal(ArtistMatchOutcome.NotFound, entry.Outcome);
        Assert.Null(entry.Candidate);
    }

    [Fact]
    public async Task RunAsync_ChecksEveryNameWithTheSmallLimit()
    {
        var catalog = new FakeCatalog(name => Artists((name, name)));
        var probe = Build(catalog);

        var report = await probe.RunAsync(["A", "B", "C"], null, null, 0, CancellationToken.None);

        Assert.True(report.Completed);
        Assert.Equal(3, report.Total);
        Assert.Equal(3, report.Checked);
        Assert.Equal(3, report.Exact);
        Assert.Equal(100, report.ExactPercent);
        Assert.All(catalog.Limits, limit => Assert.Equal(ArtistCoverageProbe.SearchLimit, limit));
    }

    [Fact]
    public async Task RunAsync_StopsAtTheFirstRefusal()
    {
        var catalog = new FakeCatalog(name => name == "B" ? throw new CatalogRateLimitedException() : Artists((name, name)));
        var probe = Build(catalog);

        var report = await probe.RunAsync(["A", "B", "C"], null, null, 0, CancellationToken.None);

        Assert.False(report.Completed);
        Assert.Equal(3, report.Total);
        Assert.Equal(2, report.Checked);
        Assert.Equal(1, report.Exact);
        Assert.Equal(1, report.RateLimited);
        Assert.Equal(["A", "B"], catalog.Searches);
        Assert.Equal(100, report.ExactPercent); // the unanswered name is not a miss
    }

    [Fact]
    public async Task RunAsync_PercentagesCountOnlyAnsweredNames()
    {
        var catalog = new FakeCatalog(name => name switch
        {
            "exact" => Artists(("1", "exact")),
            "relaxed" => Artists(("2", "RELAXED")),
            "missing" => Artists(),
            _ => Artists(("3", "someone else")),
        });
        var probe = Build(catalog);

        var report = await probe.RunAsync(["exact", "relaxed", "missing", "other"], null, null, 0, CancellationToken.None);

        Assert.Equal(1, report.Exact);
        Assert.Equal(1, report.Relaxed);
        Assert.Equal(1, report.NotFound);
        Assert.Equal(1, report.Unmatched);
        Assert.Equal(25, report.ExactPercent);
        Assert.Equal(50, report.RelaxedPercent);
    }

    [Fact]
    public async Task RunAsync_CheckpointsPartialReportsAndReportsProgress()
    {
        var catalog = new FakeCatalog(name => Artists((name, name)));
        var probe = Build(catalog);
        var checkpoints = new List<ArtistCoverageReport>();
        var progress = new List<double>();

        var report = await probe.RunAsync(
            ["A", "B", "C", "D", "E"],
            new Progress(progress),
            partial =>
            {
                checkpoints.Add(partial);
                return Task.CompletedTask;
            },
            2,
            CancellationToken.None);

        Assert.Equal([2, 4], checkpoints.Select(partial => partial.Checked));
        Assert.All(checkpoints, partial => Assert.False(partial.Completed));
        Assert.True(report.Completed);
        Assert.Equal(100, progress[^1]);
    }

    [Fact]
    public async Task Store_RoundTripsTheReport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apple-music-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ArtistCoverageStore(Path.Combine(directory, "artist-coverage.json"));
            Assert.Null(await store.LoadAsync(CancellationToken.None));

            var catalog = new FakeCatalog(name => name == "B" ? Artists() : Artists((name, name)));
            var report = await Build(catalog).RunAsync(["A", "B"], null, null, 0, CancellationToken.None);
            await store.SaveAsync(report, CancellationToken.None);

            var loaded = await store.LoadAsync(CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal(report.StartedAt, loaded.StartedAt);
            Assert.Equal(2, loaded.Total);
            Assert.Equal(1, loaded.Exact);
            Assert.Equal(1, loaded.NotFound);
            Assert.Equal(ArtistMatchOutcome.NotFound, loaded.Entries[1].Outcome);
            Assert.False(File.Exists(store.Path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ArtistCoverageProbe Build(IAppleMusicCatalog catalog)
        => new(catalog, NullLogger<ArtistCoverageProbe>.Instance);

    private static IReadOnlyList<CatalogItem<ArtistAttributes>> Artists(params (string Id, string Name)[] artists)
        => artists.Select(artist => new CatalogItem<ArtistAttributes>(artist.Id, "jp", new ArtistAttributes { Name = artist.Name })).ToList();

    private sealed class Progress : IProgress<double>
    {
        private readonly List<double> _values;

        public Progress(List<double> values)
        {
            _values = values;
        }

        public void Report(double value)
            => _values.Add(value);
    }

    private sealed class FakeCatalog : IAppleMusicCatalog
    {
        private readonly Func<string, IReadOnlyList<CatalogItem<ArtistAttributes>>> _responder;

        public FakeCatalog(Func<string, IReadOnlyList<CatalogItem<ArtistAttributes>>> responder)
        {
            _responder = responder;
        }

        public List<string> Searches { get; } = [];

        public List<int> Limits { get; } = [];

        public Task<IReadOnlyList<CatalogItem<ArtistAttributes>>> SearchArtistsAsync(string term, int limit, CancellationToken cancellationToken)
        {
            Searches.Add(term);
            Limits.Add(limit);
            return Task.FromResult(_responder(term));
        }

        public Task<IReadOnlyList<CatalogItem<ArtistAttributes>>> SearchArtistsAsync(string term, CancellationToken cancellationToken)
            => throw new NotSupportedException("The probe must ask with an explicit limit.");

        public Task<IReadOnlyList<CatalogItem<SongAttributes>>> SearchSongsAsync(string term, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CatalogItem<AlbumAttributes>>> SearchAlbumsAsync(string term, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CatalogItem<SongAttributes>?> GetSongAsync(string id, string? storefront, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CatalogItem<AlbumAttributes>?> GetAlbumAsync(string id, string? storefront, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CatalogItem<ArtistAttributes>?> GetArtistAsync(string id, string? storefront, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
