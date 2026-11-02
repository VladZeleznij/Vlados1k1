using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MarchLauncher;

/// <summary>
/// Local development SSO for march_auth on the SAME Windows computer as MTA.
/// No MTA fetchRemote loopback HTTP calls (often blocked as error 1006).
/// A single-use credential ticket lives briefly in the server resource folder,
/// then MTA deletes it BEFORE checking credentials against its existing database.
/// NOT suitable for remote/public authentication servers.
/// </summary>
internal sealed class LauncherAuthHandoff : IDisposable
{
    private const string TicketName = "launcher_sso_pending.json";
    private const string ResultName = "launcher_sso_result.json";
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(120);

    private sealed record Pending(string Id, string Folder, DateTimeOffset Expiry);
    private Pending? _pending;

    public bool HasPending => _pending is { } p && DateTimeOffset.UtcNow < p.Expiry;
    public event Action<bool, string>? Completed;

    public void Prepare(string mode, string username, string password, string selectedResourceFolder, string mtaFolder)
    {
        if (mode is not ("login" or "register"))
            throw new ArgumentException("Unsupported mode", nameof(mode));

        var folder = FindResourceFolder(selectedResourceFolder, mtaFolder);
        if (folder is null)
            throw new DirectoryNotFoundException(
                "Не найдена папка серверного ресурса march_auth. Убедись, что MTA-сервер запущен на этом компьютере и ресурс установлен.");

        var item = new Pending(Guid.NewGuid().ToString("N"), folder, DateTimeOffset.UtcNow.Add(Lifetime));
        var path = Path.Combine(folder, TicketName);
        var temp = Path.Combine(folder, "launcher_sso_pending." + item.Id + ".tmp");

        // Avoid sharing credentials with MTA's client downloads or the HTTP
        // resource server: this file is intentionally NOT declared in meta.xml.
        var data = JsonSerializer.Serialize(new
        {
            ok = true,
            id = item.Id,
            mode,
            username,
            password,
            expiresAt = item.Expiry.ToUnixTimeSeconds()
        });
        try
        {
            File.WriteAllText(temp, data, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true); // atomically publish complete ticket
            _pending = item;                        // no password retained in the record
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void PollResult()
    {
        var item = _pending;
        if (item is null) return;

        var path = Path.Combine(item.Folder, ResultName);
        if (File.Exists(path))
        {
            try
            {
                var content = File.ReadAllText(path, Encoding.UTF8);
                using var json = JsonDocument.Parse(content);
                var root = json.RootElement;
                if (root.GetProperty("id").GetString() == item.Id)
                {
                    var success = root.GetProperty("ok").GetBoolean();
                    var message = root.GetProperty("message").GetString() ?? "invalid_request";
                    _pending = null;
                    File.Delete(path);
                    Completed?.Invoke(success, message);
                    return;
                }
            }
            catch (IOException) { }        // server may still be writing
            catch (JsonException) { }      // server may still be writing
            catch (KeyNotFoundException) { }
            catch (InvalidOperationException) { }
        }

        if (DateTimeOffset.UtcNow >= item.Expiry)
        {
            _pending = null;
            try
            {
                var pendingPath = Path.Combine(item.Folder, TicketName);
                if (File.Exists(pendingPath) && File.ReadAllText(pendingPath).Contains(item.Id, StringComparison.Ordinal))
                    File.Delete(pendingPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Completed?.Invoke(false, "handoff_expired");
        }
    }

    public static string? FindResourceFolder(string configured, string mtaFolder)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) roots.Add(configured);
        if (!string.IsNullOrWhiteSpace(mtaFolder)) roots.Add(mtaFolder);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName.Contains("MTA Server", StringComparison.OrdinalIgnoreCase) ||
                    process.ProcessName.Contains("mtaserver", StringComparison.OrdinalIgnoreCase))
                {
                    var executable = process.MainModule?.FileName;
                    if (executable is not null) roots.Add(Path.GetDirectoryName(executable)!);
                }
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }

        roots.Add(AppContext.BaseDirectory);
        foreach (var candidate in roots)
        {
            try
            {
                if (!Directory.Exists(candidate)) continue;
                var baseDir = Path.GetFullPath(candidate);
                if (string.Equals(Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar)),
                    "march_auth", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(Path.Combine(baseDir, "meta.xml")))
                    return baseDir;

                var dir = baseDir;
                for (var depth = 0; depth < 3 && dir is not null; depth++)
                {
                    var resources = Path.Combine(dir, "mods", "deathmatch", "resources");
                    if (Directory.Exists(resources))
                    {
                        var found = SearchResources(resources);
                        if (found is not null) return found;
                    }
                    dir = Directory.GetParent(dir)?.FullName;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    private static string? SearchResources(string resources)
    {
        var direct = Path.Combine(resources, "march_auth");
        if (File.Exists(Path.Combine(direct, "meta.xml"))) return direct;

        // Some MTA servers organize resources in [categories].
        foreach (var category in Directory.EnumerateDirectories(resources))
        {
            if (!Path.GetFileName(category).StartsWith("[", StringComparison.Ordinal)) continue;
            var nested = Path.Combine(category, "march_auth");
            if (File.Exists(Path.Combine(nested, "meta.xml"))) return nested;
        }
        return null;
    }

    public void Dispose() { /* ticket remains valid if player closes launcher while game loads */ }
}
