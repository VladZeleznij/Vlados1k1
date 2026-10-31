using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace MarchLauncher;

public partial class MainWindow : Window
{
    private readonly string _settingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MARCH ROLE PLAY", "launcher-settings.json");

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFile)) return;
            var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_settingsFile));
            if (settings is null) return;
            MtaPathBox.Text = settings.MtaPath ?? "";
            HostBox.Text = string.IsNullOrWhiteSpace(settings.Host) ? "127.0.0.1" : settings.Host;
            PortBox.Text = settings.Port.ToString();
        }
        catch { StatusText.Text = "Не удалось прочитать сохранённые настройки"; }
    }

    private void SaveSettings(int port)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        var settings = new LauncherSettings(MtaPathBox.Text.Trim(), HostBox.Text.Trim(), port);
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
        var host = HostBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host) || host.Contains('/') || host.Contains(' ') || host.Contains(':'))
        {
            StatusText.Text = "Укажи IP-адрес или домен сервера без протокола и порта.";
            return;
        }
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            StatusText.Text = "Укажи правильный порт MTA (например, 22003).";
            return;
        }

        try
        {
            SaveSettings(port);
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
    private void AccountButton_Click(object sender, RoutedEventArgs e) => StatusText.Text = "Личный кабинет появится после подключения march_auth.";
    private void SettingsButton_Click(object sender, RoutedEventArgs e) { HostBox.Focus(); StatusText.Text = "Настройки сервера находятся справа."; }

    private sealed record LauncherSettings(string MtaPath, string Host, int Port);
}