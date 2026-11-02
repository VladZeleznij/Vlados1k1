using System.IO;
using System.Windows;
using System.Windows.Input;

namespace MarchLauncher;

/// <summary>
/// Separate settings window. Only installation directory and install-mode
/// preference are implemented. No downloader or file writes are triggered.
/// </summary>
public partial class SettingsWindow : Window
{
    private string _installDirectory;

    public string InstallDirectory => _installDirectory;
    public string InstallMode => UpdateInstallOption.IsChecked == true ? "update" : "full";

    public SettingsWindow(string? installDirectory, string? installMode)
    {
        InitializeComponent();
        _installDirectory = installDirectory ?? "";
        InstallPathBox.Text = string.IsNullOrWhiteSpace(_installDirectory)
            ? "Папка пока не выбрана"
            : _installDirectory;
        if (installMode == "update")
            UpdateInstallOption.IsChecked = true;
        else
            FullInstallOption.IsChecked = true;
    }

    private void BrowseInstallFolder_Click(object sender, RoutedEventArgs e)
    {
        using var picker = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выберите папку для будущей установки MARCH ROLE PLAY",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_installDirectory) ? _installDirectory : ""
        };
        if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        _installDirectory = picker.SelectedPath;
        InstallPathBox.Text = _installDirectory;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Save the preference only; do not create folders or install files.
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
