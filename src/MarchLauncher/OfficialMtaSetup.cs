using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Win32;

namespace MarchLauncher;

/// <summary>
/// Installs the official MTA release into MARCH ROLE PLAY/MTA San Andreas.
/// GTA SA is never downloaded: it must be owned and already installed by the user.
/// </summary>
internal static class OfficialMtaSetup
{
    // Link behind the official "Download (Win10+)" button on multitheftauto.com.
    private static readonly Uri MtaInstallerUrl = new(
        "https://mirror-cdn.multitheftauto.com/mtasa/main/mtasa-1.6.exe");

    private static readonly HttpClient Http = new(
        new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(25)
    };

    internal sealed record ProgressInfo(string Message, int Percentage);

    public static string GameRoot(char drive) =>
        Path.Combine(char.ToUpperInvariant(drive) + ":\\", "MARCH ROLE PLAY");

    public static IReadOnlyList<char> AvailableDisks() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => char.ToUpperInvariant(d.Name[0]))
            .Distinct()
            .OrderBy(c => c)
            .ToArray();

    private static bool IsGameDirectory(string path) =>
        File.Exists(Path.Combine(path, "gta_sa.exe")) &&
        File.Exists(Path.Combine(path, "models", "gta3.img"));

    public static string? FindOwnedGame(char selectedDrive, string destination)
    {
        var root = char.ToUpperInvariant(selectedDrive) + ":\\";
        var candidates = new List<string>();
        // Trust only existing local installation metadata and well-known paths.
        foreach (var baseKey in new[] {
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default)
        })
        {
            try
            {
                using var reg = RegistryKey.OpenBaseKey(baseKey.Item1, baseKey.Item2);
                foreach (var (keyName, valueName) in new[] {
                    (@"SOFTWARE\Rockstar Games\GTA San Andreas\Installation", "ExePath"),
                    (@"SOFTWARE\Multi Theft Auto: San Andreas All\Common", "GTA:SA Path"),
                    (@"SOFTWARE\Multi Theft Auto: San Andreas", "GTA:SA Path")
                })
                {
                    using var key = reg.OpenSubKey(keyName);
                    var value = key?.GetValue(valueName) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        var path = value.Trim().Trim('"');
                        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            path = Path.GetDirectoryName(path) ?? "";
                        candidates.Add(path);
                    }
                }
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Optional discovery only; never need administrator rights to search.
            }
        }

        candidates.AddRange(new[] {
            @"Games\GTA San Andreas",
            @"Games\Grand Theft Auto San Andreas",
            @"Rockstar Games\GTA San Andreas",
            @"Program Files\Rockstar Games\GTA San Andreas",
            @"Program Files (x86)\Rockstar Games\GTA San Andreas",
            @"SteamLibrary\steamapps\common\Grand Theft Auto San Andreas",
            @"Program Files (x86)\Steam\steamapps\common\Grand Theft Auto San Andreas"
        }.Select(s => Path.Combine(root, s)));

        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (Path.GetPathRoot(full)?.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                        != true ||
                    !IsGameDirectory(full) ||
                    string.Equals(full.TrimEnd('\\'), destination.TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                return full;
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or IOException)
            {
                // Invalid legacy registry value.
            }
        }
        return null;
    }

    public static void MoveOwnedGameInPlace(string oldFolder, string destination)
    {
        oldFolder = Path.GetFullPath(oldFolder).TrimEnd('\\', '/');
        destination = Path.GetFullPath(destination).TrimEnd('\\', '/');
        if (!IsGameDirectory(oldFolder))
            throw new InvalidOperationException("Не найдена оригинальная GTA SA.");
        if (File.Exists(Path.Combine(destination, "gta_sa.exe")) ||
            (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()))
            throw new InvalidOperationException(
                "Целевая папка GTA уже существует и не пуста. Ничего не перезаписано.");
        if (!string.Equals(Path.GetPathRoot(oldFolder), Path.GetPathRoot(destination),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Перемещение без копирования возможно только в пределах одного диска.");
        if ((File.GetAttributes(oldFolder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Нельзя перемещать папку-ссылку.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (Directory.Exists(destination)) Directory.Delete(destination);
        // Atomic rename on the same volume: no second copy of the licensed game.
        Directory.Move(oldFolder, destination);
    }

    public static async Task InstallMtaAsync(
        string gameRoot, IProgress<ProgressInfo> progress, CancellationToken cancel)
    {
        var gta = GameClientInstaller.GtaDirectory(gameRoot);
        var mta = GameClientInstaller.MtaDirectory(gameRoot);
        if (!IsGameDirectory(gta))
            throw new InvalidOperationException(
                "Сначала нужна лицензионная GTA San Andreas в папке проекта.");

        if (GameClientInstaller.HasLocalMta(gameRoot))
        {
            progress.Report(new ProgressInfo("MTA уже установлен в папке проекта.", 100));
            return;
        }

        // The official installer reads the machine-wide GTA path first. Never override
        // that path silently, as that could break another user's MTA installation.
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                RegistryView.Registry32).OpenSubKey(
                    @"SOFTWARE\Multi Theft Auto: San Andreas All\Common");
            var registered = key?.GetValue("GTA:SA Path") as string;
            if (!string.IsNullOrWhiteSpace(registered) &&
                !string.Equals(Path.GetFullPath(registered.Trim().Trim('"')).TrimEnd('\\'),
                    Path.GetFullPath(gta).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "В Windows другой путь GTA для MTA. Автоматическая установка не будет менять его без разрешения. " +
                    "Укажи нашу папку GTA в официальном установщике MTA.");
        }
        catch (System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                "Нет доступа к проверке пути GTA в реестре Windows.");
        }

        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MARCH ROLE PLAY", "downloads");
        Directory.CreateDirectory(cache);
        var file = Path.Combine(cache, "mta-official-1.6.exe");
        var partial = file + ".partial";

        try
        {
            progress.Report(new ProgressInfo("Скачиваем официальный MTA 1.6...", 0));
            using var response = await Http.GetAsync(
                MtaInstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 300_000_000)
                throw new InvalidDataException("Размер установщика MTA превышает лимит.");

            await using (var input = await response.Content.ReadAsStreamAsync(cancel))
            await using (var output = new FileStream(part, FileMode.Create,
                FileAccess.Write, FileShare.None, 131072, useAsync: true))
            {
                var buffer = new byte[131072];
                long size = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancel)) > 0)
                {
                    size += count;
                    if (size > 300_000_000)
                        throw new InvalidDataException("Недопустимый размер файла MTA.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancel);
                    var percent = response.Content.Headers.ContentLength is > 0 ?
                        (int)Math.Min(85, size * 85 / response.Content.Headers.ContentLength.Value) : 0;
                    progress.Report(new ProgressInfo(
                        $"Загрузка MTA: {size / 1048576} МБ", percent));
                }
                if (size < 20_000_000)
                    throw new InvalidDataException("Установщик MTA скачан не полностью.");
            }

            // Only accept a Windows executable, and only from the hardcoded official HTTPS URL.
            await using (var input = File.OpenRead(part))
            {
                if (input.ReadByte() != 'M' || input.ReadByte() != 'Z')
                    throw new InvalidDataException("Неправильный формат установщика MTA.");
            }
            File.Move(part, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }

        // Installer uses HKCU when there is no existing HKLM GTA path.
        // Do not write a machine-wide path here.
        using (var key = Registry.CurrentUser.CreateSubKey(
            @"SOFTWARE\Multi Theft Auto: San Andreas"))
            key?.SetValue("GTA:SA Path", gta, RegistryValueKind.String);

        Directory.CreateDirectory(mta);
        progress.Report(new ProgressInfo("Устанавливаем официальный MTA...", 87));
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = file,
            // NSIS silent install; /D= must be the last parameter, unquoted.
            Arguments = "/S /D=" + mta,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(file)!
        }) ?? throw new IOException("Не удалось запустить установщик MTA.");

        await process.WaitForExitAsync(CancellationToken.None);
        if (process.ExitCode != 0 || !GameClientInstaller.HasLocalMta(gameRoot))
            throw new InvalidOperationException(
                "Установщик MTA завершился без подтверждённой установки. " +
                "Проверь журнал Windows и используй официальный установщик.");
        progress.Report(new ProgressInfo("MTA установлен в MARCH ROLE PLAY.", 100));
    }
}
