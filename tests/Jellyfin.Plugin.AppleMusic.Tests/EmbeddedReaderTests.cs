using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata;
using Jellyfin.Plugin.AppleMusic.Providers.EmbeddedMetadata;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class EmbeddedReaderTests
{
    [Fact]
    public async Task Reader_UsesArgumentsWithoutShellAndParsesStreamTags()
    {
        using var reader = new FfprobeEmbeddedMetadataReader(() => "/server/ffprobe", (start, token) =>
        {
            Assert.False(start.UseShellExecute);
            Assert.Equal("/server/ffprobe", start.FileName);
            Assert.Equal("/music/a ' $(touch nope).ogg", start.ArgumentList[^1]);
            Assert.Contains("format_tags:stream=codec_type:stream_tags", start.ArgumentList);
            return Task.FromResult("""{"streams":[{"codec_type":"audio","tags":{"track":"18","artist":"Artist"}}]}""");
        });
        var result = await reader.ReadAsync("/music/a ' $(touch nope).ogg", new EmbeddedTagOptions(), TestContext.Current.CancellationToken);
        Assert.Equal(18, result.Track);
        Assert.Equal(["Artist"], result.Artists);
    }

    [Fact]
    public async Task Reader_CancelsWaitingWorkWithoutStartingAnotherProcess()
    {
        var entered = 0;
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var reader = new FfprobeEmbeddedMetadataReader(() => "ffprobe", async (_, token) =>
        {
            if (Interlocked.Increment(ref entered) == 2)
            {
                bothEntered.SetResult();
            }

            await Task.Delay(Timeout.Infinite, token);
            return "{}";
        });
        var first = reader.ReadAsync("one", new EmbeddedTagOptions(), cancellation.Token);
        var second = reader.ReadAsync("two", new EmbeddedTagOptions(), cancellation.Token);
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var third = reader.ReadAsync("three", new EmbeddedTagOptions(), cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        Assert.Equal(2, entered);
    }

    public static bool HasShell => File.Exists("/bin/sh");

    [Fact(Skip = "Requires a POSIX shell for the child-process fixture.", SkipUnless = nameof(HasShell))]
    public async Task Process_ReadsBothPipesAndReportsFailure()
    {
        var start = Shell("printf '%s' '{\"format\":{}}'; printf diagnostic >&2");
        Assert.Equal("{\"format\":{}}", await FfprobeEmbeddedMetadataReader.RunProcessAsync(start, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<IOException>(() => FfprobeEmbeddedMetadataReader.RunProcessAsync(Shell("exit 7"), TestContext.Current.CancellationToken));
    }

    [Fact(Skip = "Requires a POSIX shell for the child-process fixture.", SkipUnless = nameof(HasShell))]
    public async Task Process_CancellationTerminatesChildAndReapsIt()
    {
        // A generated marker proves the process started before cancellation;
        // no audio fixture or installed ffprobe is required for lifecycle tests.
        var marker = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pid");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var start = Shell("echo $$ > \"$1\"; exec sleep 30");
        start.ArgumentList.Add("probe-test");
        start.ArgumentList.Add(marker);
        var run = FfprobeEmbeddedMetadataReader.RunProcessAsync(start, cancellation.Token);
        try
        {
            while (!File.Exists(marker))
            {
                await Task.Delay(10, cancellation.Token);
            }

            var text = await File.ReadAllTextAsync(marker, cancellation.Token);
            while (string.IsNullOrWhiteSpace(text))
            {
                await Task.Delay(10, cancellation.Token);
                text = await File.ReadAllTextAsync(marker, cancellation.Token);
            }

            var pid = int.Parse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture);
            using var child = Process.GetProcessById(pid);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            Assert.True(child.HasExited);
        }
        finally
        {
            await cancellation.CancelAsync();
            try
            {
                await run;
            }
            catch (OperationCanceledException)
            {
                // Cleanup must also run when an assertion or startup check fails.
            }

            File.Delete(marker);
        }
    }

    [Fact(Skip = "Requires a POSIX shell for the child-process fixture.", SkipUnless = nameof(HasShell))]
    public async Task Process_OversizedErrorPipeFailsPromptly()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<IOException>(() => FfprobeEmbeddedMetadataReader.RunProcessAsync(Shell("yes oversized-diagnostic >&2 & wait"), deadline.Token));
    }

    private static ProcessStartInfo Shell(string command)
    {
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(command);
        return start;
    }
}
