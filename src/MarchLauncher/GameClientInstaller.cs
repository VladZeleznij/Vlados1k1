using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MarchLauncher;

/// <summary>
/// Builds an isolated copy from the player's own GTA:SA installation and installs
/// project-owned updates. No GTA/MTA binaries are redistributed.
/// </summary>
internal static class GameClientInstaller
{
    // Published by the project, not editable by players from the UI.
    private static readonly Uri ManifestUrl = new(
        "https://raw.githubusercontent.com/VladZeleznij/Vlados1k1/main/client/manifest.json");

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    internal sealed record InstallProgress(string Message, int Percent);

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

    public static async Task CopyOwnedGameAsync(
        string source, string installRoot, IProgress<InstallProgress> progress,
        CancellationToken cancel)
    {
        source = Path.GetFullPath(source.Trim());
        installRoot = Path.GetFullPath(installRoot.Trim());
        if (!File.Exists(Path.Combine(source, "gta_sa.exe")) ||
            !File.Exists(Path.Combine(source, "models", "gta3.img")))
            throw new InvalidOperationException(
                "Выбери папку с легально установленной GTA San Andreas (gta_sa.exe и models\\gta3.img).");

        var destination = Path.Combine(installRoot, "GTA San Andreas");
        if (Overlaps(source, installRoot) || Overlaps(source, destination))
            throw new InvalidOperationException(
                "Папка клиента должна находиться отдельно от исходной GTA San Andreas.");

        var files = await Task.Run(() => EnumerateSafeFiles(new DirectoryInfo(source))
            .Select(f => (Source: f.FullName, Relative: Path.GetRelativePath(source, f.FullName),
                          Size: f.Length, Modified: f.LastWriteTimeUtc)).ToArray(), cancel);

        if (files.Length == 0) throw new InvalidOperationException("В исходной GTA нет файлов.");
        Directory.CreateDirectory(destination);
        var index = 0;

        await Task.Run(() =>
        {
            foreach (var file in files)
            {
                cancel.ThrowIfCancellationRequested();
                var output = Path.Combine(destination, file.Relative);
                EnsureSafeTarget(destination, output);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                if (File.Exists(output))
                {
                    var existing = new FileInfo(output);
                    if (existing.Length == file.Size &&
                        existing.LastWriteTimeUtc == file.Modified)
                    {
                        index++;
                        progress.Report(new InstallProgress(
                            "Проверено: " + file.Relative, index * 100 / files.Length));
                        continue;
                    }
                }

                var temp = output + ".march-part";
                try
                {
                    File.Copy(file.Source, temp, overwrite: true);
                    File.SetLastWriteTimeUtc(temp, file.Modified);
                    File.Move(temp, output, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                index++;
                progress.Report(new InstallProgress(
                    "Установлено: " + file.Relative, index * 100 / files.Length));
            }
        }, cancel);

        progress.Report(new InstallProgress(
            "Копия GTA SA готова. Исходная игра не изменена.", 100));
    }

    public static async Task UpdateProjectFilesAsync(
        string installRoot, IProgress<InstallProgress> progress,
        CancellationToken cancel)
    {
        installRoot = Path.GetFullPath(installRoot.Trim());
        if (!File.Exists(Path.Combine(installRoot, "GTA San Andreas", "gta_sa.exe")))
            throw new InvalidOperationException(
                "Сначала установи отдельную копию GTA SA через «Полная установка».");

        progress.Report(new InstallProgress("Загрузка списка обновлений...", 0));
        using var manifestResponse = await Http.GetAsync(
            ManifestUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
        manifestResponse.EnsureSuccessStatusCode();
        await using var manifestStream =
            await manifestResponse.Content.ReadAsStreamAsync(cancel);
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(
            manifestStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancel);
        if (manifest?.Files == null || manifest.Files.Count > 10000)
            throw new InvalidDataException("Манифест обновлений имеет неверный формат.");

        var root = Path.GetFullPath(installRoot);
        var index = 0;
        foreach (var file in manifest.Files)
        {
            cancel.ThrowIfCancellationRequested();
            var dest = CheckManifestPath(root, file.Path);
            if (file.Size < 0 ||
                file.Size > 4L * 1024 * 1024 * 1024 ||
                file.Sha256.Length != 64 ||
                !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Неверные параметры файла: " + file.Path);

            if (File.Exists(dest) &&
                await HasExpectedHashAsync(dest, file.Sha256, cancel))
            {
                index++;
                progress.Report(new InstallProgress(
                    "Проверено: " + file.Path,
                    index * 100 / Math.Max(1, manifest.Files.Count)));
                continue;
            }

            var url = new Uri(ManifestUrl, file.Url);
            if (url.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Обновления должны загружаться по HTTPS.");

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            EnsureSafeTarget(root, dest);
            var temp = dest + ".march-part";
            try
            {
                using var response = await Http.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancel);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(cancel))
                await using (var output = new FileStream(temp, FileMode.Create,
                    FileAccess.Write, FileShare.None, 131072, useAsync: true))
                {
                    await input.CopyToAsync(output, cancel);
                }

                if (new FileInfo(temp).Length != file.Size ||
                    !await HasExpectedHashAsync(temp, file.Sha256, cancel))
                    throw new InvalidDataException(
                        "Файл не прошёл проверку размера/SHA-256: " + file.Path);

                EnsureSafeTarget(root, dest);
                File.Move(temp, dest, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }

            index++;
            progress.Report(new InstallProgress(
                "Обновлено: " + file.Path,
                index * 100 / Math.Max(1, manifest.Files.Count)));
        }

        progress.Report(new InstallProgress(
            "Файлы проекта проверены. Версия: " + manifest.Version, 100));
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path, string expected, CancellationToken cancel)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            131072, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancel);
        return string.Equals(Convert.ToHexString(hash), expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CheckManifestPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) ||
            Path.IsPathFullyQualified(relative) ||
            relative.Contains(':') ||
            relative.Split('/', '\\').Any(s => s is "" or "." or ".."))
            throw new InvalidDataException("Недопустимый путь в манифесте: " + relative);

        var dest = Path.GetFullPath(Path.Combine(root,
            relative.Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar)));
        EnsureSafeTarget(root, dest);

        // Never allow a remote manifest to modify the licensed game, launcher
        // binaries, credentials or user settings. Only game project assets.
        var assets = Path.Combine(root, "MARCH Assets") +
            Path.DirectorySeparatorChar;
        if (!dest.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Обновления разрешены только внутри папки MARCH Assets.");
        return dest;
    }

    private static void EnsureSafeTarget(string root, string path)
    {
        root = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        path = Path.GetFullPath(path);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("Попытка записи за пределами папки клиента.");

        for (var parent = Path.GetDirectoryName(path);
             parent != null && parent.Length >= root.Length;
             parent = Path.GetDirectoryName(parent))
        {
            if (Directory.Exists(parent) &&
                (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Символьные ссылки в папке установки запрещены.");
            if (string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                break;
        }
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Нельзя перезаписывать ссылку на другой файл.");
    }

    private static bool Overlaps(string a, string b)
    {
        a = Path.GetFullPath(a).TrimEnd('\\', '/');
        b = Path.GetFullPath(b).TrimEnd('\\', '/');
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
               a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<FileInfo> EnumerateSafeFiles(DirectoryInfo directory)
    {
        foreach (var item in directory.EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            if (item is FileInfo file)
                yield return file;
            else if (item is DirectoryInfo subDirectory)
                foreach (var child in EnumerateSafeFiles(subDirectory))
                    yield return child;
        }
    }
}
