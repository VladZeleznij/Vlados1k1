using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace MarchLauncher;

/// <summary>
/// Checks the project-owned HTTPS manifest, compares every asset by SHA-256,
/// and installs only missing or changed files. Never downloads GTA or MTA binaries.
/// </summary>
internal static class ProjectUpdateService
{
    private static readonly Uri ManifestUrl = new(
        "https://raw.githubusercontent.com/VladZeleznij/Vlados1k1/main/client/manifest.json");

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    private static readonly SemaphoreSlim UpdateLock = new(1, 1);

    private static readonly HashSet<string> AllowedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".dff", ".txd", ".col", ".img", ".png", ".jpg", ".jpeg",
        ".webp", ".dds", ".bmp", ".tga", ".ogg", ".mp3", ".wav",
        ".json", ".xml", ".txt", ".dat", ".ide", ".ipl"
    };

    internal sealed record ProgressInfo(string Message, int Percent);

    internal sealed record PendingAsset(
        string RelativePath, string FullPath, Uri DownloadUrl,
        string Sha256, long Size, bool Missing);

    internal sealed record UpdatePlan(
        string Version, int TotalFiles, IReadOnlyList<PendingAsset> Pending,
        int Missing, int Damaged, long DownloadBytes);

    internal sealed record UpdateResult(
        string Version, int TotalFiles, int DownloadedFiles, long DownloadedBytes);

    private sealed class Manifest
    {
        public string Version { get; set; } = "";
        public List<ManifestFile> Files { get; set; } = new();
    }

    private sealed class ManifestFile
    {
        public string Path { get; set; } = "";
        public string Url { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Size { get; set; }
    }

    public static async Task<UpdateResult> CheckAndRepairAsync(
        string installRoot, IProgress<ProgressInfo>? progress, CancellationToken cancel)
    {
        // Serializes updates from the main button, the settings tab, and Play.
        await UpdateLock.WaitAsync(cancel);
        try
        {
            var plan = await CheckAsync(installRoot, progress, cancel);
            return await InstallAsync(installRoot, plan, progress, cancel);
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    public static async Task<UpdatePlan> CheckAsync(
        string installRoot, IProgress<ProgressInfo>? progress, CancellationToken cancel)
    {
        var root = Path.GetFullPath(installRoot.Trim());
        if (!File.Exists(Path.Combine(GameClientInstaller.GtaDirectory(root), "gta_sa.exe")) ||
            !File.Exists(Path.Combine(GameClientInstaller.GtaDirectory(root), "models", "gta3.img")))
            throw new InvalidOperationException(
                "Не найдена GTA San Andreas в папке проекта. Сначала выполни полную установку.");

        progress?.Report(new ProgressInfo("Получение списка файлов с сервера обновлений...", 0));

        using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await Http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();

        // A server-side manifest is intentionally small. Prevent unbounded allocations.
        const int manifestLimit = 1024 * 1024;
        if (response.Content.Headers.ContentLength > manifestLimit)
            throw new InvalidDataException("Слишком большой файл списка обновлений.");

        await using var manifestStream = await response.Content.ReadAsStreamAsync(cancel);
        using var collected = new MemoryStream();
        var tempBuffer = new byte[16384];
        int read;
        while ((read = await manifestStream.ReadAsync(tempBuffer, cancel)) > 0)
        {
            if (collected.Length + read > manifestLimit)
                throw new InvalidDataException("Список обновлений превышает допустимый размер.");
            collected.Write(tempBuffer, 0, read);
        }

        collected.Position = 0;
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(
            collected, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancel);
        if (manifest == null || manifest.Files == null || manifest.Files.Count > 10000 ||
            string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("Сервер вернул некорректный список обновлений.");

        // Validate the complete manifest before reading or writing any assets.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<PendingAsset>(manifest.Files.Count);
        foreach (var entry in manifest.Files)
        {
            cancel.ThrowIfCancellationRequested();
            var destination = SafeDestination(root, entry.Path);
            if (!seen.Add(destination))
                throw new InvalidDataException("Повторяющийся файл в списке: " + entry.Path);
            if (entry.Size < 0 || entry.Size > 4L * 1024 * 1024 * 1024 ||
                entry.Sha256 == null || entry.Sha256.Length != 64 ||
                !entry.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Некорректные данные: " + entry.Path);

            if (string.IsNullOrWhiteSpace(entry.Url))
                throw new InvalidDataException("Нет ссылки для файла " + entry.Path);
            var url = new Uri(ManifestUrl, entry.Url);
            if (url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length != 0)
                throw new InvalidDataException("Для обновления необходим HTTPS: " + entry.Path);

            entries.Add(new PendingAsset(entry.Path, destination, url,
                entry.Sha256, entry.Size, false));
        }

        var pending = new List<PendingAsset>();
        var missing = 0;
        var damaged = 0;
        long bytes = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var item = entries[i];
            progress?.Report(new ProgressInfo(
                $"Проверка файлов {i + 1}/{entries.Count}: {item.RelativePath}",
                entries.Count == 0 ? 100 : (i + 1) * 70 / entries.Count));

            CheckNoLinks(root, item.FullPath);
            var absent = !File.Exists(item.FullPath);
            var good = !absent &&
                new FileInfo(item.FullPath).Length == item.Size &&
                await MatchesHashAsync(item.FullPath, item.Sha256, cancel);
            if (good) continue;

            if (absent) missing++;
            else damaged++;
            pending.Add(item with { Missing = absent });
            bytes = checked(bytes + item.Size);
        }

        return new UpdatePlan(manifest.Version, entries.Count, pending,
            missing, damaged, bytes);
    }

    public static async Task<UpdateResult> InstallAsync(
        string installRoot, UpdatePlan plan,
        IProgress<ProgressInfo>? progress, CancellationToken cancel)
    {
        var root = Path.GetFullPath(installRoot.Trim());
        long downloaded = 0;
        var finished = 0;
        foreach (var item in plan.Pending)
        {
            cancel.ThrowIfCancellationRequested();
            // Revalidate path before each write, including symbolic-link checks.
            var dest = SafeDestination(root, item.RelativePath);
            if (!string.Equals(dest, item.FullPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Путь установки изменился.");
            CheckNoLinks(root, dest);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            CheckNoLinks(root, dest);

            var part = dest + ".march-part";
            CheckNoLinks(root, part);
            try
            {
                using var response = await Http.GetAsync(
                    item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > item.Size)
                    throw new InvalidDataException("Размер загрузки превышает манифест: " +
                        item.RelativePath);
                await using (var input = await response.Content.ReadAsStreamAsync(cancel))
                await using (var output = new FileStream(part, FileMode.Create,
                    FileAccess.Write, FileShare.None, 131072, useAsync: true))
                {
                    var buffer = new byte[131072];
                    long fileBytes = 0;
                    int n;
                    while ((n = await input.ReadAsync(buffer, cancel)) > 0)
                    {
                        fileBytes += n;
                        if (fileBytes > item.Size)
                            throw new InvalidDataException("Файл превышает заявленный размер: " +
                                item.RelativePath);
                        await output.WriteAsync(buffer.AsMemory(0, n), cancel);
                        var percent = plan.DownloadBytes <= 0 ? 95 :
                            70 + (int)Math.Min(29,
                                (downloaded + fileBytes) * 29.0 / plan.DownloadBytes);
                        progress?.Report(new ProgressInfo(
                            $"Загрузка {finished + 1}/{plan.Pending.Count}: {item.RelativePath} " +
                            $"({fileBytes / 1024}/{Math.Max(1, item.Size / 1024)} КБ)", percent));
                    }
                }

                if (new FileInfo(part).Length != item.Size ||
                    !await MatchesHashAsync(part, item.Sha256, cancel))
                    throw new InvalidDataException(
                        "Сервер прислал повреждённый файл (SHA-256): " + item.RelativePath);

                CheckNoLinks(root, dest);
                File.Move(part, dest, overwrite: true);
            }
            finally
            {
                if (File.Exists(part)) File.Delete(part);
            }

            downloaded += item.Size;
            finished++;
            progress?.Report(new ProgressInfo(
                $"Установлено {finished}/{plan.Pending.Count}: {item.RelativePath}",
                plan.DownloadBytes <= 0 ? 99 :
                    70 + (int)Math.Min(29, downloaded * 29.0 / plan.DownloadBytes)));
        }

        progress?.Report(new ProgressInfo(
            plan.Pending.Count == 0
                ? $"Все {plan.TotalFiles} файлов актуальны. Версия {plan.Version}"
                : $"Установлено {finished} файлов. Версия {plan.Version}", 100));
        return new UpdateResult(plan.Version, plan.TotalFiles, finished, downloaded);
    }

    private static async Task<bool> MatchesHashAsync(
        string path, string expected, CancellationToken cancel)
    {
        await using var file = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read, 131072, useAsync: true);
        var digest = await SHA256.HashDataAsync(file, cancel);
        return Convert.ToHexString(digest).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeDestination(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathFullyQualified(relative) ||
            relative.Contains(':') || relative.Contains('\0') ||
            relative.Split('/', '\\').Any(s => s is "" or "." or ".."))
            throw new InvalidDataException("Недопустимый путь в манифесте: " + relative);

        var full = Path.GetFullPath(Path.Combine(root,
            relative.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)));
        var assets = Path.GetFullPath(Path.Combine(root,
            GameClientInstaller.AssetsFolderName)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase) ||
            !AllowedExtensions.Contains(Path.GetExtension(full)))
            throw new InvalidDataException("Обновление разрешено только для игровых ресурсов: " +
                relative);
        return full;
    }

    private static void CheckNoLinks(string root, string full)
    {
        root = Path.GetFullPath(root).TrimEnd('\\', '/');
        full = Path.GetFullPath(full);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
            throw new IOException("Недопустимый путь установки.");

        // Protect against directory junctions / symlink redirections.
        for (string? dir = Path.GetDirectoryName(full);
             dir != null && dir.Length >= root.Length;
             dir = Path.GetDirectoryName(dir))
        {
            if (Directory.Exists(dir) &&
                (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("В папке клиента обнаружена ссылка: " + dir);
            if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
                break;
        }
        if (File.Exists(full) &&
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Нельзя обновить файл-ссылку.");
    }
}
