using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace osuautodeafen.cs.Changelog;

public static class VideoPreviewCache
{
    private static readonly SemaphoreSlim EncodeGate =
        new(Math.Clamp(Environment.ProcessorCount / 2, 1, 3));

    private static readonly CancellationTokenSource GlobalCts = new();

    private static readonly HttpClient Http = new();

    private static readonly string BaseCacheRoot =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen",
            "changelog-cache");

    private static string GetVersionRoot(string version)
    {
        string path = Path.Combine(BaseCacheRoot, version);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetPreviewPath(string url, string version)
    {
        return Path.Combine(GetVersionRoot(version), Hash(url) + ".webp");
    }

    public static void EnsurePreview(
        string url,
        string version,
        Action<double>? progress,
        Action<string>? completed)
    {
        _ = Task.Run(async () =>
        {
            string root = GetVersionRoot(version);
            CleanupIfTooLarge(root, 50_000_000);

            string hash = Hash(url);
            string output = Path.Combine(root, hash + ".webp");
            string poster = Path.Combine(root, hash + ".jpg");

            if (url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(output) || new FileInfo(output).Length == 0)
                {
                    try
                    {
                        await using var stream = await Http.GetStreamAsync(url, GlobalCts.Token);
                        await using var fs = File.Create(output);
                        await stream.CopyToAsync(fs, GlobalCts.Token);
                    }
                    catch
                    {
                        if (File.Exists(output))
                            File.Delete(output);
                        return;
                    }
                }

                progress?.Invoke(1.0);
                completed?.Invoke(output);
                return;
            }

            if (File.Exists(output))
            {
                if (new FileInfo(output).Length > 0)
                {
                    progress?.Invoke(1.0);
                    completed?.Invoke(output);
                    return;
                }

                File.Delete(output);
            }

            if (File.Exists(poster))
            {
                completed?.Invoke(poster);
            }

            progress?.Invoke(0.05);

            await EncodeGate.WaitAsync(GlobalCts.Token);
            try
            {
                if (!File.Exists(poster))
                {
                    await GeneratePosterFrameAsync(url, poster, GlobalCts.Token);
                    completed?.Invoke(poster);
                }

                progress?.Invoke(0.2);

                await GenerateWebPFromUrlAsync(
                    url,
                    output,
                    p =>
                    {
                        double eased = 1.0 - Math.Pow(1.0 - p, 2.2);
                        progress?.Invoke(0.2 + eased * 0.75);
                    },
                    GlobalCts.Token
                );

                if (!File.Exists(output) || new FileInfo(output).Length == 0)
                {
                    if (File.Exists(output))
                        File.Delete(output);
                    return;
                }

                progress?.Invoke(1.0);
                completed?.Invoke(output);
            }
            finally
            {
                EncodeGate.Release();
            }
        }, GlobalCts.Token);
    }

    public static void DeleteOldChangelogCaches(string currentVersion)
    {
        if (!Directory.Exists(BaseCacheRoot))
            return;

        foreach (string dir in Directory.GetDirectories(BaseCacheRoot))
        {
            string name = Path.GetFileName(dir);
            if (string.Equals(name, currentVersion, StringComparison.Ordinal))
                continue;

            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // ignore
            }
        }
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
                             ?? throw new InvalidOperationException();

        double p = 0.0;

        while (!proc.StandardError.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            await proc.StandardError.ReadLineAsync();
            p = Math.Min(p + 0.035, 0.97);
            progress?.Invoke(p);
        }

        await proc.WaitForExitAsync(ct);
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
                             ?? throw new InvalidOperationException();

        await proc.WaitForExitAsync(ct);
    }

    private static void CleanupIfTooLarge(string root, long maxBytes)
    {
        try
        {
            if (!Directory.Exists(root))
                return;

            FileInfo[] files = new DirectoryInfo(root).GetFiles("*.webp");
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
}
