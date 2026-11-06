using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MarchLauncher;

/// <summary>
/// Starts an installed MTA client directly with its supported mtasa://
/// argument instead of depending exclusively on Windows URL association.
/// No protocol registry changes, admin privileges or external downloads.
/// </summary>
internal static class MtaClientLauncher
{
    public const string LocalServerUri = "mtasa://127.0.0.1:22003";

    /// <summary>
    /// Starts precisely the MTA installation selected by the player.
    /// No fallback to another client, no file repair and no GTA modification.
    /// This does not bypass MTA's own integrity or anti-cheat checks.
    /// </summary>
    public static string StartSelected(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new DirectoryNotFoundException("Папка MTA не выбрана.");
        var absolute = Path.GetFullPath(directory.Trim());
        var executable = Path.Combine(absolute, "Multi Theft Auto.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException(
                "В выбранной папке не найден Multi Theft Auto.exe. Выбери папку установленного MTA.",
                executable);

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = absolute,
            UseShellExecute = true
        };
        start.ArgumentList.Add(LocalServerUri);
        using var process = Process.Start(start);
        if (process is null)
            throw new InvalidOperationException("Windows не удалось запустить выбранный клиент MTA.");

        return "Запущен выбранный клиент MTA: " + absolute;
    }

    public static string Start(string? savedMtaDirectory, string? projectInstallDirectory)
    {
        var failures = new List<string>();

        foreach (var candidate in DiscoverClientExecutables(savedMtaDirectory, projectInstallDirectory))
        {
            if (!File.Exists(candidate))
                continue;
            try
            {
                var workingDirectory = Path.GetDirectoryName(candidate)!;
                var startInfo = new ProcessStartInfo(candidate)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true
                };
                // MTA officially supports: "Multi Theft Auto.exe" mtasa://host:port
                startInfo.ArgumentList.Add(LocalServerUri);

                using var process = Process.Start(startInfo);
                if (process is not null)
                    return "Клиент MTA запущен. Подключение к 127.0.0.1:22003.";
                failures.Add("Не удалось запустить " + candidate);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException
                or UnauthorizedAccessException or InvalidOperationException)
            {
                failures.Add(Path.GetFileName(candidate) + ": " + ex.Message);
            }
        }

        // Keep the original URL approach when its association is available,
        // e.g. with unusual MTA installations we could not locate on disk.
        try
        {
            using var process = Process.Start(new ProcessStartInfo(LocalServerUri)
                { UseShellExecute = true });
            if (process is not null)
                return "Открывается установленный MTA через Windows.";
        }
        catch (Exception ex) when (ex is Win32Exception or IOException
            or InvalidOperationException)
        {
            failures.Add("Протокол mtasa://: " + ex.Message);
        }

        var diagnostic = failures.Count > 0
            ? " Детали: " + string.Join(" | ", failures.Take(2))
            : "";
        throw new InvalidOperationException(
            "Не найден установленный клиент MTA San Andreas. " +
            "Установи официальный MTA или проверь его установку. " +
            "Лаунчер автоматически ищет Multi Theft Auto.exe через Windows." +
            diagnostic);
    }

    private static IEnumerable<string> DiscoverClientExecutables(
        string? savedMtaDirectory, string? projectInstallDirectory)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        void AddPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var candidate = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            try
            {
                candidate = Path.GetFullPath(candidate);
                var executable = string.Equals(Path.GetFileName(candidate),
                    "Multi Theft Auto.exe", StringComparison.OrdinalIgnoreCase)
                    ? candidate
                    : Path.Combine(candidate, "Multi Theft Auto.exe");
                if (paths.Add(executable)) candidates.Add(executable);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                or PathTooLongException or SecurityException) { }
        }

        // Prefer an already configured client or the self-contained project
        // client (if installed) before any other installation.
        AddPath(savedMtaDirectory);
        if (!string.IsNullOrWhiteSpace(projectInstallDirectory))
        {
            try { AddPath(GameClientInstaller.MtaDirectory(projectInstallDirectory)); }
            catch (Exception ex) when (ex is ArgumentException or IOException) { }
        }

        // Official MTA installers remember the last installation directory.
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                foreach (var version in new[] { "1.7", "1.6", "1.5" })
                {
                    AddRegistryValue(hive, view,
                        @"SOFTWARE\Multi Theft Auto: San Andreas All\" + version,
                        "Last Install Location", AddPath);
                    AddRegistryValue(hive, view,
                        @"SOFTWARE\Multi Theft Auto: San Andreas " + version,
                        "Last Install Location", AddPath);
                }

                AddRegistryValue(hive, view,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Multi Theft Auto.exe",
                    "", AddPath);

                // Even a broken mtasa URL association can contain the real
                // executable path. Never invoke the registry command itself.
                AddRegistryValue(hive, view,
                    @"SOFTWARE\Classes\mtasa\shell\open\command",
                    "", value => AddPath(ParseExecutablePath(value)));
            }
        }

        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"mtasa\shell\open\command");
            AddPath(ParseExecutablePath(key?.GetValue(null) as string));
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
            or IOException) { }

        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (var root in programFiles.Where(x => !string.IsNullOrWhiteSpace(x)))
            foreach (var version in new[] { "1.7", "1.6", "1.5" })
                AddPath(Path.Combine(root, "MTA San Andreas " + version));

        return candidates;
    }

    private static void AddRegistryValue(RegistryHive hive, RegistryView view,
        string subkey, string valueName, Action<string?> addPath)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(subkey);
            addPath(key?.GetValue(valueName) as string);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException
            or IOException or ArgumentException) { }
    }

    private static string? ParseExecutablePath(string? registryCommand)
    {
        if (string.IsNullOrWhiteSpace(registryCommand)) return null;
        // Handles both the usual quoted command and MTA installer commands
        // which sometimes omit a space before "%1".
        var match = Regex.Match(registryCommand,
            @"^\s*""(?<path>[^""]+?\.exe)""", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups["path"].Value;

        match = Regex.Match(registryCommand,
            @"^\s*(?<path>.+?\.exe)(?:\s|""|$)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["path"].Value : null;
    }
}
