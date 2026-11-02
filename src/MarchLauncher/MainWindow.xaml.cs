using System.Diagnostics;
using System.Net;
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
    private LauncherAuthHandoff? _authHandoff;
    private string _authHandoffError = "";
    private readonly DispatcherTimer _monitorTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _authResultTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _checkingServer;

    public MainWindow()
    {
        InitializeComponent();
        _discordPresence.StatusChanged += message =>
            Dispatcher.BeginInvoke(new Action(() => DiscordPresenceText.Text = message));
        LoadSettings();
        try
        {
            _authHandoff = new LauncherAuthHandoff();
            _authHandoff.Completed += (ok, code) => HandleGameAuthResult(ok, code);
            _authResultTimer.Tick += (_, _) => _authHandoff.PollResult();
            _authResultTimer.Start();
        }
        catch (Exception ex)
        {
            _authHandoffError = ex.Message;
            LauncherLoginStatusText.Text = "Мост MTA недоступен: " + ex.Message;
            LauncherRegisterStatusText.Text = LauncherLoginStatusText.Text;
        }
        _discordPresence.Configure(DiscordApplicationId);
        _monitorTimer.Tick += async (_, _) => await UpdateServerStatusAsync();
        _monitorTimer.Start();
        Loaded += async (_, _) => await UpdateServerStatusAsync();
        Closed += (_, _) => { _monitorTimer.Stop(); _authResultTimer.Stop(); _discordPresence.Dispose(); _authHandoff?.Dispose(); };
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
            AuthResourcePathBox.Text = settings.AuthResourcePath ?? "";
            _telegramUrl = settings.TelegramUrl ?? "";
            _vkUrl = settings.VkUrl ?? "";
            if (settings.RememberUsername && !string.IsNullOrWhiteSpace(settings.Username))
            {
                LoginUsernameBox.Text = settings.Username;
                RememberLoginBox.IsChecked = true;
            }
        }
        catch { StatusText.Text = "Не удалось прочитать сохранённые настройки"; }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        var settings = new LauncherSettings(MtaPathBox.Text.Trim(), "127.0.0.1", 22003,
            DiscordApplicationId, _telegramUrl, _vkUrl, DiscordInviteUrl,
            RememberLoginBox.IsChecked == true ? LoginUsernameBox.Text.Trim() : "",
            RememberLoginBox.IsChecked == true,
            AuthResourcePathBox.Text.Trim());
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

    private static string DescribeAuthResult(string code)
    {
        return code switch
        {
            "login_success" => "Вход выполнен! MTA авторизовал тебя автоматически.",
            "registration_success" => "Аккаунт создан и вход в MTA выполнен!",
            "username_exists" => "Этот логин уже занят. Выбери другой.",
            "custom_auth_not_integrated" => "Регистрация через лаунчер требует подключения к существующей базе march_auth. Новая база не создана.",
            "invalid_credentials" => "Проверь логин и пароль.",
            "invalid_credentials_or_external_db" =>
                "Логин не подтверждён. Возможно, сервер использует отдельную базу march_auth.",
            "missing_acl" => "Серверу не хватает ACL-права function.addAccount.",
            "auth_denied" => "Вход отклонён сервером. Проверь права function.logIn.",
            "db_unavailable" => "База аккаунтов march_auth недоступна.",
            "db_error" => "Ошибка чтения базы аккаунтов march_auth.",
            "hash_error" => "Не удалось обработать пароль для регистрации.",
            "already_logged_in" => "Аккаунт уже авторизован в игре.",
            "invalid_request" => "Неправильный запрос авторизации.",
            "handoff_expired" => "MTA не принял данные в течение двух минут. Проверь версию march_auth и папку сервера.",
            "rate_limited" => "Слишком частые попытки входа. Подожди несколько секунд.",
            _ => "Ответ сервера: " + code
        };
    }

    private void HandleGameAuthResult(bool ok, string code)
    {
        var message = DescribeAuthResult(code);
        LauncherLoginStatusText.Text = message;
        LauncherRegisterStatusText.Text = message;
        StatusText.Text = message;
        if (ok)
        {
            LoginPasswordBox.Clear();
            RegisterPasswordBox.Clear();
            RegisterRepeatPasswordBox.Clear();
        }
    }

    private static bool ValidateCredentials(string mode, string username, string password, out string error)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[A-Za-z0-9_]{3,20}$"))
        {
            error = "Логин: 3–20 символов, латинские буквы, цифры или _.";
            return false;
        }
        // march_auth v1.5.2 accepts 1–64 characters for existing native MTA
        // accounts, but requires 8–64 for new SQLite/bcrypt registrations.
        var minimumPasswordLength = mode == "register" ? 8 : 1;
        if (password.Length < minimumPasswordLength || password.Length > 64)
        {
            error = mode == "register"
                ? "Для регистрации пароль должен содержать от 8 до 64 символов."
                : "Пароль должен содержать от 1 до 64 символов.";
            return false;
        }
        error = "";
        return true;
    }

    private bool PrepareLauncherAuth(string mode, string username, string password)
    {
        var target = mode == "login" ? LauncherLoginStatusText : LauncherRegisterStatusText;
        if (_authHandoff == null)
        {
            target.Text = "Мост авторизации недоступен: " + _authHandoffError;
            return false;
        }
        username = username.Trim();
        if (!ValidateCredentials(mode, username, password, out var error))
        {
            target.Text = error;
            return false;
        }

        try
        {
            _authHandoff.Prepare(mode, username, password,
                AuthResourcePathBox.Text.Trim(), MtaPathBox.Text.Trim());
            if (mode == "login") SaveSettings();
            target.Text = "Данные для входа подготовлены. Нажми «ИГРАТЬ»: " +
                "march_auth проверит аккаунт автоматически.";
            StatusText.Text = "Автовход подготовлен. Нажми «ИГРАТЬ».";
            return true;
        }
        catch (Exception ex)
        {
            target.Text = "Не удалось подготовить авторизацию: " + ex.Message;
            StatusText.Text = target.Text;
            return false;
        }
    }

    private void LoginActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (PrepareLauncherAuth("login", LoginUsernameBox.Text, LoginPasswordBox.Password))
            LaunchMta();
    }

    private void RegisterActionButton_Click(object sender, RoutedEventArgs e)
    {
        var password = RegisterPasswordBox.Password;
        if (!string.Equals(password, RegisterRepeatPasswordBox.Password, StringComparison.Ordinal))
        {
            LauncherRegisterStatusText.Text = "Пароли не совпадают.";
            return;
        }
        if (PrepareLauncherAuth("register", RegisterUsernameBox.Text, password))
            LaunchMta();
    }

    private void BrowseAuthResource_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выбери папку march_auth на компьютере с MTA-сервером",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        if (!File.Exists(Path.Combine(dialog.SelectedPath, "meta.xml")) ||
            !string.Equals(Path.GetFileName(dialog.SelectedPath.TrimEnd('\\')),
                "march_auth", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Нужно выбрать именно папку ресурса march_auth с meta.xml.";
            return;
        }
        AuthResourcePathBox.Text = dialog.SelectedPath;
        SaveSettings();
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
        // No separate "ВОЙТИ" click is required: the Play button stages the
        // credentials directly from the currently selected auth form.
        if (_authHandoff?.HasPending != true)
        {
            if (RightTabs.SelectedIndex == 1)
            {
                var login = RegisterUsernameBox.Text.Trim();
                var password = RegisterPasswordBox.Password;
                if (login.Length > 0 || password.Length > 0)
                {
                    if (password != RegisterRepeatPasswordBox.Password)
                    {
                        LauncherRegisterStatusText.Text = "Пароли не совпадают.";
                        return;
                    }
                    if (!PrepareLauncherAuth("register", login, password)) return;
                }
            }
            else
            {
                var login = LoginUsernameBox.Text.Trim();
                var password = LoginPasswordBox.Password;
                if (login.Length > 0 || password.Length > 0)
                {
                    if (!PrepareLauncherAuth("login", login, password)) return;
                }
            }
        }
        LaunchMta();
    }

    private void LaunchMta()
    {
        const string host = "127.0.0.1";
        const int port = 22003;
        try
        {
            SaveSettings();
            Process.Start(new ProcessStartInfo($"mtasa://{host}:{port}")
                { UseShellExecute = true });
            StatusText.Text = _authHandoff?.HasPending == true
                ? "MTA запускается. Данные переданы локальному серверу, ждём подтверждение входа."
                : "MTA запускается. Ввод пароля в игре доступен как обычно.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось открыть MTA: " + ex.Message;
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
    private void AccountButton_Click(object sender, RoutedEventArgs e) { RightTabs.SelectedIndex = 0; StatusText.Text = "Введи данные для входа и нажми «ИГРАТЬ»."; }
    private void SettingsButton_Click(object sender, RoutedEventArgs e) { RightTabs.SelectedIndex = 2; MtaPathBox.Focus(); StatusText.Text = "Настройки сервера находятся справа."; }

    private sealed record LauncherSettings(
        string MtaPath, string Host, int Port,
        string? DiscordAppId = null,
        string? TelegramUrl = null,
        string? VkUrl = null,
        string? DiscordInviteUrl = null,
        string? Username = null,
        bool RememberUsername = false,
        string? AuthResourcePath = null);
}