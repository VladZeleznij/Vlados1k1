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
    private readonly string _settingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MARCH ROLE PLAY", "launcher-settings.json");

    private readonly DispatcherTimer _monitorTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool _checkingServer;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        _monitorTimer.Tick += async (_, _) => await UpdateServerStatusAsync();
        _monitorTimer.Start();
        Loaded += async (_, _) => await UpdateServerStatusAsync();
        Closed += (_, _) => _monitorTimer.Stop();
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
                    var length = data[index++];
                    if (index + length > data.Length) throw new InvalidDataException("Неполный ASE-ответ");
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
            ServerStateText.Text = "Онлайн";
            ServerVersionText.Text = string.IsNullOrWhiteSpace(result.Version) ? "—" : result.Version;
            ServerNameText.Text = string.IsNullOrWhiteSpace(result.Name) ? "MARCH ROLE PLAY" : result.Name;
        }
        catch (Exception)
        {
            ServerMonitorText.Text = "●  Сервер недоступен";
            ServerMonitorText.Foreground = System.Windows.Media.Brushes.Orange;
            ServerPlayersText.Text = "— / —";
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
        }
        catch { StatusText.Text = "Не удалось прочитать сохранённые настройки"; }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        var settings = new LauncherSettings(MtaPathBox.Text.Trim(), "127.0.0.1", 22003);
        File.WriteAllText(_settingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

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

    private sealed record LauncherSettings(string MtaPath, string Host, int Port);
}