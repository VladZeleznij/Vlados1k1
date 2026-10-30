using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
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
            MtaPathBox.Text = settings.MtaPath;
            HostBox.Text = settings.Host;
            PortBox.Text = settings.Port.ToString();
        }
        catch
        {
            StatusText.Text = "Не удалось загрузить настройки";
        }
    }

    private void SaveSettings(int port)
    {
        var directory = Path.GetDirectoryName(_settingsFile)!;
        Directory.CreateDirectory(directory);
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

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text.Trim();
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            StatusText.Text = "Укажите корректный порт сервера";
            return;
        }

        var mtaPath = MtaPathBox.Text.Trim();
        var clientExe = Path.Combine(mtaPath, "Multi Theft Auto.exe");
        if (!File.Exists(clientExe))
        {
            StatusText.Text = "Не найден Multi Theft Auto.exe — выберите правильную папку";
            return;
        }

        PlayButton.IsEnabled = false;
        try
        {
            StatusText.Text = "Проверяем доступность сервера...";
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(4000));
            if (completed != connectTask)
            {
                StatusText.Text = "Сервер не отвечает. Проверьте адрес и порт.";
                return;
            }
            await connectTask;
            SaveSettings(port);

            Process.Start(new ProcessStartInfo
            {
                FileName = clientExe,
                Arguments = $"mtasa://{host}:{port}",
                WorkingDirectory = mtaPath,
                UseShellExecute = true
            });
            StatusText.Text = "Запускаем MTA...";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка запуска: " + ex.Message;
        }
        finally
        {
            PlayButton.IsEnabled = true;
        }
    }

    private sealed record LauncherSettings(string MtaPath, string Host, int Port);
}
