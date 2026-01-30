using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace osuautodeafen.cs.Changelog;

public static class VideoPreviewCache
{
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
        if (!url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            return;

        _ = Task.Run(async () =>
        {
            string root = GetVersionRoot(version);
            CleanupIfTooLarge(root, 50_000_000);

            string output = GetPreviewPath(url, version);

            if (File.Exists(output) && new FileInfo(output).Length > 0)
            {
                progress?.Invoke(1.0);
                completed?.Invoke(output);
                return;
            }

            try
            {
                using var response = await Http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    GlobalCts.Token);

                response.EnsureSuccessStatusCode();

                long? contentLength = response.Content.Headers.ContentLength;

                await using var input = await response.Content.ReadAsStreamAsync(GlobalCts.Token);
                await using var outputFs = File.Create(output);

                byte[] buffer = new byte[81920];
                long readTotal = 0;

                while (true)
                {
                    int read = await input.ReadAsync(buffer, GlobalCts.Token);
                    if (read == 0)
                        break;

                    await outputFs.WriteAsync(buffer.AsMemory(0, read), GlobalCts.Token);
                    readTotal += read;

                    if (contentLength.HasValue)
                        progress?.Invoke(Math.Min(0.99, (double)readTotal / contentLength.Value));
                }

                progress?.Invoke(1.0);
                completed?.Invoke(output);
            }
            catch
            {
                if (File.Exists(output))
                    File.Delete(output);
            }
        }, GlobalCts.Token);
    }

    public static void DeleteOldChangelogCaches(string currentVersion)
    {
        if (!Directory.Exists(BaseCacheRoot))
            return;

        foreach (string dir in Directory.GetDirectories(BaseCacheRoot))
        {
            if (Path.GetFileName(dir) == currentVersion)
                continue;

            try { Directory.Delete(dir, true); }
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
            // ignored
        }
    }

    private static string Hash(string input)
    {
        using SHA1 sha = SHA1.Create();
        return Convert.ToHexString(
            sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }
}
