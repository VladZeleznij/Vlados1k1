using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Windows.Threading;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace MarchLauncher;

public partial class MainWindow : Window
{
    // Project-owned identifiers: users cannot replace these through local settings.
    private const string DiscordApplicationId = "1301565059113418763";
    private const string DiscordInviteUrl = "https://discord.gg/GMkC5YGXku";

    // Legacy social links are still respected if they were configured previously.
    private string _telegramUrl = "";
    private string _vkUrl = "";
    private readonly string _settingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MARCH ROLE PLAY", "launcher-settings.json");

    private readonly DiscordPresence _discordPresence = new();
    private readonly DispatcherTimer _monitorTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool _checkingServer;

    public MainWindow()
    {
        InitializeComponent();
        _discordPresence.StatusChanged += message =>
            Dispatcher.BeginInvoke(new Action(() => DiscordPresenceText.Text = message));
        LoadSettings();
        _discordPresence.Configure(DiscordApplicationId);
        _monitorTimer.Tick += async (_, _) => await UpdateServerStatusAsync();
        _monitorTimer.Start();
        Loaded += async (_, _) => await UpdateServerStatusAsync();
        Closed += (_, _) => { _monitorTimer.Stop(); _discordPresence.Dispose(); };
    }

    private async Task UpdateServerStatusAsync()
    {
        if (_checkingServer) return;
        _checkingServer = true;
        try
        {
            var result = await Task.Run(async () =>
            {
                using var udp = new UdpClient();
                udp.Connect("127.0.0.1", 22126);
                await udp.SendAsync(new byte[] { (byte)'s' });
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var packet = await udp.ReceiveAsync(timeout.Token);
                var data = packet.Buffer;
                if (data.Length < 8 || Encoding.ASCII.GetString(data, 0, 4) != "EYE1")
                    throw new InvalidDataException("Некорректный ASE-ответ");
                var index = 4;
                string ReadField()
                {
                    if (index >= data.Length) throw new InvalidDataException("Неполный ASE-ответ");
                    var length = data[index++] - 1; // ASE length includes its own prefix byte
                    if (length < 0 || index + length > data.Length) throw new InvalidDataException("Неполный ASE-ответ");
                    var value = Encoding.UTF8.GetString(data, index, length);
                    index += length;
                    return value;
                }
                _ = ReadField(); // game identifier
                _ = ReadField(); // server port
                var name = ReadField();
                _ = ReadField(); // game mode
                _ = ReadField(); // map
                var version = ReadField();
                _ = ReadField(); // password flag
                var players = ReadField();
                var slots = ReadField();
                if (!int.TryParse(players, out var online) || !int.TryParse(slots, out var max)
                    || online < 0 || max < 1 || online > max || max > 10000)
                    throw new InvalidDataException("Некорректное количество игроков");
                return (Name: name, Version: version, Online: online, Max: max);
            });
            ServerMonitorText.Text = "●  Сервер онлайн";
            ServerMonitorText.Foreground = System.Windows.Media.Brushes.SpringGreen;
            ServerPlayersText.Text = $"{result.Online} / {result.Max}";
            _discordPresence.UpdateServerOnline(result.Online, result.Max);
            ServerStateText.Text = "Онлайн";
            ServerVersionText.Text = string.IsNullOrWhiteSpace(result.Version) ? "—" : result.Version;
            ServerNameText.Text = string.IsNullOrWhiteSpace(result.Name) ? "MARCH ROLE PLAY" : result.Name;
        }
        catch (Exception ex)
        {
            ServerMonitorText.Text = ex is OperationCanceledException ? "●  ASE не отвечает (UDP 22126)" : "●  Ошибка ASE: " + ex.Message;
            ServerMonitorText.Foreground = System.Windows.Media.Brushes.Orange;
            ServerPlayersText.Text = "— / —";
            _discordPresence.UpdateServerOnline(null, null);
            ServerStateText.Text = "Оффлайн";
            ServerVersionText.Text = "—";
            ServerNameText.Text = "MARCH ROLE PLAY";
        }
        finally { _checkingServer = false; }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFile)) return;
            var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_settingsFile));
            if (settings is null) return;
            MtaPathBox.Text = settings.MtaPath ?? "";
            _telegramUrl = settings.TelegramUrl ?? "";
            _vkUrl = settings.VkUrl ?? "";
        }
        catch { StatusText.Text = "Не удалось прочитать сохранённые настройки"; }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        var settings = new LauncherSettings(MtaPathBox.Text.Trim(), "127.0.0.1", 22003,
            DiscordApplicationId, _telegramUrl, _vkUrl, DiscordInviteUrl);
        File.WriteAllText(_settingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool ValidCommunityLink(string value, params string[] domains)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            domains.Any(domain => string.Equals(uri.Host, domain, StringComparison.OrdinalIgnoreCase));
    }

    private void OpenCommunity(string network, string link, params string[] allowedDomains)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            StatusText.Text = "Ссылка " + network + " пока не добавлена в лаунчер.";
            return;
        }
        if (!ValidCommunityLink(link, allowedDomains))
        {
            StatusText.Text = "Ссылка " + network + " недействительна.";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(link.Trim()) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText.Text = "Не удалось открыть " + network + ": " + ex.Message; }
    }

    private void TelegramButton_Click(object sender, RoutedEventArgs e) =>
        OpenCommunity("Telegram", _telegramUrl, "t.me", "telegram.me");

    private void VkButton_Click(object sender, RoutedEventArgs e) =>
        OpenCommunity("ВКонтакте", _vkUrl, "vk.com", "m.vk.com");

    private void DiscordButton_Click(object sender, RoutedEventArgs e) =>
        OpenCommunity("Discord", DiscordInviteUrl, "discord.gg", "discord.com");

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выберите папку Multi Theft Auto",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            MtaPathBox.Text = dialog.SelectedPath;
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        const string host = "127.0.0.1";
        const int port = 22003;

        try
        {
            SaveSettings();
            var uri = $"mtasa://{host}:{port}";
            // Use the Windows-registered MTA protocol handler: passing mtasa:// as a
            // raw argument to Multi Theft Auto.exe does not reliably connect.
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            StatusText.Text = "MTA запускается. Подключаемся к " + host + ":" + port;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось открыть MTA: " + ex.Message +
                ". Установи MTA и запусти её хотя бы один раз.";
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void NewsButton_Click(object sender, RoutedEventArgs e) => StatusText.Text = "Новости проекта отображаются на главной.";
    private void PromoButton_Click(object sender, RoutedEventArgs e) => StatusText.Text = "Промокоды будут доступны после подключения серверного API.";
    private void AccountButton_Click(object sender, RoutedEventArgs e) { RightTabs.SelectedIndex = 0; StatusText.Text = "Авторизация появится после подключения march_auth."; }
    private void SettingsButton_Click(object sender, RoutedEventArgs e) { RightTabs.SelectedIndex = 2; MtaPathBox.Focus(); StatusText.Text = "Настройки сервера находятся справа."; }

    private sealed record LauncherSettings(
        string MtaPath, string Host, int Port,
        string? DiscordAppId = null,
        string? TelegramUrl = null,
        string? VkUrl = null,
        string? DiscordInviteUrl = null);
}