using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata;
using MediaBrowser.Controller.MediaEncoding;
using TagSnapshot = Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata.EmbeddedMetadata;

namespace Jellyfin.Plugin.AppleMusic.Providers.EmbeddedMetadata;

internal sealed class FfprobeEmbeddedMetadataReader : IEmbeddedMetadataReader, IDisposable
{
    private readonly SemaphoreSlim _gate = new(2, 2);
    private readonly Func<string> _probePath;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<string>> _run;

    internal FfprobeEmbeddedMetadataReader(IMediaEncoder encoder)
        : this(() => encoder.ProbePath, RunProcessAsync)
    {
    }

    internal FfprobeEmbeddedMetadataReader(Func<string> probePath, Func<ProcessStartInfo, CancellationToken, Task<string>> run)
    {
        _probePath = probePath;
        _run = run;
    }

    public async Task<TagSnapshot> ReadAsync(string path, EmbeddedTagOptions options, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var start = new ProcessStartInfo(_probePath())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            // ArgumentList keeps names containing quotes or shell syntax literal.
            foreach (var argument in new[] { "-v", "error", "-of", "json", "-show_entries", "format_tags:stream=codec_type:stream_tags", "-i", path })
            {
                start.ArgumentList.Add(argument);
            }

            try
            {
                var json = await _run(start, deadline.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return EmbeddedTagParser.Parse(json, options);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Embedded metadata probe exceeded 30 seconds.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    internal static async Task<string> RunProcessAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        process.Start();
        // Drain both pipes concurrently; a noisy corrupt file must not deadlock.
        var stdout = ReadBoundedAsync(process.StandardOutput, cancellationToken);
        var stderr = ReadBoundedAsync(process.StandardError, cancellationToken);
        try
        {
            // Observe whichever pipe fails first, even if the other remains open.
            var pending = new List<Task> { stdout, stderr, process.WaitForExitAsync(cancellationToken) };
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                pending.Remove(completed);
            }

            var output = await stdout.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                throw new IOException($"ffprobe exited with code {process.ExitCode}.");
            }

            return output;
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // A natural exit may race cleanup.
                }
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            // Observe both readers even when either pipe fails or is cancelled.
            try
            {
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                // The original read failure is propagated by the enclosing try.
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        const int limit = 4 * 1024 * 1024;
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (result.Length + count > limit)
            {
                throw new IOException("ffprobe output exceeded the metadata size limit.");
            }

            result.Append(buffer, 0, count);
        }

        return result.ToString();
    }
}
