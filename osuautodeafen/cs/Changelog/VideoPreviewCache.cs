using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace osuautodeafen.cs.ViewModels;

public static class VideoPreviewCache
{
    private static readonly SemaphoreSlim EncodeGate =
        new(Math.Clamp(Environment.ProcessorCount / 2, 1, 3));

    private static readonly CancellationTokenSource GlobalCts = new();

    private static readonly string CacheRoot =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen",
            "changelog-cache");

    static VideoPreviewCache()
    {
        Directory.CreateDirectory(CacheRoot);
        CleanupIfTooLarge(50_000_000); // 50mb
    }

    public static string GetPreviewPath(string url)
    {
        return Path.Combine(CacheRoot, Hash(url) + ".webp");
    }

    public static void EnsurePreview(
        string url,
        Action<double>? progress,
        Action<string>? completed)
    {
        _ = Task.Run(async () =>
        {
            string output = GetPreviewPath(url);
            string poster = GetPosterPath(url);

            if (File.Exists(output))
            {
                progress?.Invoke(1.0);
                completed?.Invoke(output);
                return;
            }

            progress?.Invoke(0.03);

            await EncodeGate.WaitAsync(GlobalCts.Token);
            try
            {
                if (!File.Exists(poster))
                {
                    await GeneratePosterFrameAsync(url, poster, GlobalCts.Token);
                    completed?.Invoke(poster);
                }

                progress?.Invoke(0.15);

                await GenerateWebPFromUrlAsync(
                    url,
                    output,
                    p =>
                    {
                        double eased = 1.0 - Math.Pow(1.0 - p, 2.2);
                        progress?.Invoke(0.15 + eased * 0.80);
                    },
                    GlobalCts.Token
                );

                progress?.Invoke(1.0);
                completed?.Invoke(output);
            }
            finally
            {
                EncodeGate.Release();
            }
        }, GlobalCts.Token);
    }

    public static void CancelAll()
    {
        GlobalCts.Cancel();
    }

    private static async Task GenerateWebPFromUrlAsync(
        string url,
        string output,
        Action<double>? progress,
        CancellationToken ct)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "ffmpeg",
            Arguments =
                "-y " +
                "-loglevel error " +
                "-ss 0.2 " +
                $"-i \"{url}\" " +
                "-vf \"fps=24,scale=896:-1:flags=fast_bilinear\" " +
                "-loop 0 -an " +
                "-compression_level 4 " +
                "-quality 80 " +
                "-speed 6 " +
                $"\"{output}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        double p = 0.0;

        while (!proc.StandardError.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            await proc.StandardError.ReadLineAsync();

            p = Math.Min(p + 0.035, 0.97);
            progress?.Invoke(p);
        }

        await proc.WaitForExitAsync(ct);

        if (!File.Exists(output))
            throw new InvalidOperationException("ffmpeg failed to write WebP preview.");

        progress?.Invoke(1.0);
    }

    private static void CleanupIfTooLarge(long maxBytes)
    {
        try
        {
            var files = new DirectoryInfo(CacheRoot).GetFiles("*.webp");
            long total = 0;

            foreach (FileInfo f in files)
                total += f.Length;

            if (total <= maxBytes)
                return;

            Array.Sort(files, (a, b) =>
                a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));

            foreach (FileInfo f in files)
            {
                f.Delete();
                total -= f.Length;
                if (total <= maxBytes)
                    break;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string Hash(string input)
    {
        using SHA1 sha = SHA1.Create();
        return Convert.ToHexString(
            sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }

    private static async Task GeneratePosterFrameAsync(
        string url,
        string output,
        CancellationToken ct)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "ffmpeg",
            Arguments =
                "-y " +
                "-loglevel error " +
                "-ss 0.2 " +
                $"-i \"{url}\" " +
                "-frames:v 1 " +
                "-vf \"scale=896:-1:flags=fast_bilinear\" " +
                $"\"{output}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to extract poster frame.");

        await proc.WaitForExitAsync(ct);

        if (!File.Exists(output))
            throw new InvalidOperationException("Poster frame not written.");
    }

    private static string GetPosterPath(string url)
    {
        return Path.Combine(CacheRoot, Hash(url) + ".jpg");
    }
}