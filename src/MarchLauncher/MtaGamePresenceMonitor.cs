using System.IO;
using System.Text.Json;

namespace MarchLauncher;

/// <summary>
/// Reads the local MTA server's VERIFIED, fresh march_auth player session
/// status. Never treats the launcher login field as an authenticated nickname.
/// </summary>
internal sealed class MtaGamePresenceMonitor
{
    private const string SessionFile = "launcher_discord_session.json";
    private string? _cachedResourceFolder;

    public string? GetAuthenticatedNickname(string configuredResourceFolder, string mtaFolder)
    {
        try
        {
            if (_cachedResourceFolder == null || !Directory.Exists(_cachedResourceFolder))
                _cachedResourceFolder = LauncherAuthHandoff.FindResourceFolder(
                    configuredResourceFolder, mtaFolder);

            if (_cachedResourceFolder is null) return null;

            var filePath = Path.Combine(_cachedResourceFolder, SessionFile);
            if (!File.Exists(filePath)) return null;

            // Read-only: this file contains only the nickname and timestamp;
            // passwords and other account data must never be read here.
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length == 0 || stream.Length > 4096) return null;

            using var document = JsonDocument.Parse(stream);
            var item = document.RootElement;
            if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() == 1)
                item = item[0];
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("online", out var online) ||
                online.ValueKind != JsonValueKind.True ||
                !item.TryGetProperty("updatedAt", out var updatedAt) ||
                !updatedAt.TryGetInt64(out var timestamp) ||
                !item.TryGetProperty("nickname", out var name) ||
                name.ValueKind != JsonValueKind.String)
                return null;

            var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp;
            if (age < -5 || age > 20) return null;

            var rawName = name.GetString() ?? "";
            var safeName = new string(rawName
                .Where(character => !char.IsControl(character))
                .Take(32).ToArray()).Trim();

            return string.IsNullOrWhiteSpace(safeName) ? null : safeName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException)
        {
            // The server atomically updates the status every five seconds.
            // A transient read error is not proof that anyone is authenticated.
            return null;
        }
    }
}
