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
    // Preserve previously configured paths even though the folder selection
    // controls are intentionally removed from launcher settings.
    private string _mtaFolder = "";
    private string _authResourceFolder = "";
    private string _gameInstallPath = "";
    private string _gameSourcePath = "";
    private CancellationTokenSource? _installerCancellation;
    private Task<bool>? _clientUpdateInFlight;
    private bool _launchingAfterUpdates;
    private string _gameInstallMode = "full";
    private readonly MtaGamePresenceMonitor _gamePresenceMonitor = new();
    private readonly DispatcherTimer _gamePresenceTimer =
        new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _checkingGamePresence;
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
        _gamePresenceTimer.Tick += async (_, _) => await UpdateGamePresenceAsync();
        _gamePresenceTimer.Start();
        _monitorTimer.Start();
        Loaded += async (_, _) =>
        {
            await UpdateServerStatusAsync();
            await UpdateGamePresenceAsync();
        };
        Closed += (_, _) =>
        {
            _monitorTimer.Stop();
            _gamePresenceTimer.Stop();
            _authResultTimer.Stop();
            _discordPresence.Dispose();
            _authHandoff?.Dispose();
        };
    }

    private async Task UpdateGamePresenceAsync()
    {
        if (_checkingGamePresence) return;
        _checkingGamePresence = true;
        try
        {
            var nickname = await Task.Run(() =>
                _gamePresenceMonitor.GetAuthenticatedNickname(
                    _authResourceFolder, _mtaFolder));
            // Discord shows a game nickname only when march_auth has confirmed
            // an actual local player session within the last twenty seconds.
            _discordPresence.UpdateGameSession(nickname);
        }
        catch (Exception)
        {
            _discordPresence.UpdateGameSession(null);
        }
        finally { _checkingGamePresence = false; }
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
        // Account recovery is independent of launcher-settings.json so a broken
        // settings file cannot make Windows forget a previously saved password.
        try
        {
            if (File.Exists(_settingsFile))
            {
                var settings = JsonSerializer.Deserialize<LauncherSettings>(
                    File.ReadAllText(_settingsFile));
                if (settings != null)
                {
                    _mtaFolder = settings.MtaPath ?? "";
                    _authResourceFolder = settings.AuthResourcePath ?? "";
                    _gameInstallPath = settings.GameInstallPath ?? "";
                    _gameSourcePath = settings.GameSourcePath ?? "";
                    _gameInstallMode = settings.GameInstallMode == "update"
                        ? "update" : "full";
                    _telegramUrl = settings.TelegramUrl ?? "";
                    _vkUrl = settings.VkUrl ?? "";
                    RememberLoginBox.IsChecked = settings.RememberUsername;
                    if (settings.RememberUsername &&
                        !string.IsNullOrWhiteSpace(settings.Username))
                        LoginUsernameBox.Text = settings.Username;
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка настроек. Данные Windows проверяются отдельно: " +
                ex.Message;
        }

        try
        {
            var stored = WindowsCredentialStore.ReadSaved();
            if (stored.HasValue)
            {
                LoginUsernameBox.Text = stored.Value.Username;
                LoginPasswordBox.Password = stored.Value.Password;
                RememberLoginBox.IsChecked = true;
                LauncherLoginStatusText.Text =
                    "Логин и пароль восстановлены. Нажми «ИГРАТЬ» для автовхода.";
            }
        }
        catch (Exception ex)
        {
            LauncherLoginStatusText.Text =
                "Пароль не удалось восстановить из Windows: " + ex.Message;
        }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        var settings = new LauncherSettings(_mtaFolder, "127.0.0.1", 22003,
            DiscordApplicationId, _telegramUrl, _vkUrl, DiscordInviteUrl,
            RememberLoginBox.IsChecked == true ? LoginUsernameBox.Text.Trim() : "",
            RememberLoginBox.IsChecked == true,
            _authResourceFolder,
            _gameInstallPath, _gameInstallMode, _gameSourcePath);
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
            if (code == "registration_success")
            {
                // Reuse the newly registered profile when the launcher starts
                // the next time; both registration fields are cleared below.
                LoginUsernameBox.Text = RegisterUsernameBox.Text.Trim();
                LoginPasswordBox.Password = RegisterPasswordBox.Password;
            }

            try
            {
                SaveSettings();
                if (RememberLoginBox.IsChecked == true &&
                    !string.IsNullOrWhiteSpace(LoginUsernameBox.Text) &&
                    LoginPasswordBox.Password.Length > 0)
                {
                    WindowsCredentialStore.Save(
                        LoginUsernameBox.Text.Trim(), LoginPasswordBox.Password);
                    LauncherLoginStatusText.Text = message + " Логин и пароль сохранены в Windows.";
                }
                else
                {
                    WindowsCredentialStore.Delete();
                    LoginPasswordBox.Clear();
                }
            }
            catch (Exception ex)
            {
                LauncherLoginStatusText.Text = message +
                    " Не удалось сохранить пароль: " + ex.Message;
            }

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
                _authResourceFolder, _mtaFolder);
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

    private async void LoginActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_launchingAfterUpdates) return;
        _launchingAfterUpdates = true;
        LoginActionButton.IsEnabled = false;
        try
        {
            if (PrepareLauncherAuth("login", LoginUsernameBox.Text, LoginPasswordBox.Password))
                LaunchMta();
        }
        finally
        {
            LoginActionButton.IsEnabled = true;
            _launchingAfterUpdates = false;
        }
    }

    private async void RegisterActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_launchingAfterUpdates) return;
        var password = RegisterPasswordBox.Password;
        if (!string.Equals(password, RegisterRepeatPasswordBox.Password, StringComparison.Ordinal))
        {
            LauncherRegisterStatusText.Text = "Пароли не совпадают.";
            return;
        }
        _launchingAfterUpdates = true;
        RegisterActionButton.IsEnabled = false;
        try
        {
            if (PrepareLauncherAuth("register", RegisterUsernameBox.Text, password))
                LaunchMta();
        }
        finally
        {
            RegisterActionButton.IsEnabled = true;
            _launchingAfterUpdates = false;
        }
    }

    private void RememberLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (RememberLoginBox.IsChecked != true)
            {
                WindowsCredentialStore.Delete();
                LauncherLoginStatusText.Text = "Сохранённый пароль удалён из Windows. Данные текущего входа можно использовать.";
            }
            SaveSettings();
        }
        catch (Exception ex)
        {
            LauncherLoginStatusText.Text = "Не удалось изменить сохранение пароля: " + ex.Message;
        }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_launchingAfterUpdates) return;
        _launchingAfterUpdates = true;
        PlayButton.IsEnabled = false;
        try
        {

        // If a player left the Registration tab open, still use saved LOGIN
        // credentials when no registration form was filled.
        if (RightTabs.SelectedIndex == 1 &&
            (RegisterPasswordBox.Password.Length > 0 ||
             RegisterRepeatPasswordBox.Password.Length > 0))
        {
            var password = RegisterPasswordBox.Password;
            if (password != RegisterRepeatPasswordBox.Password)
            {
                LauncherRegisterStatusText.Text = "Пароли не совпадают.";
                return;
            }
            if (!PrepareLauncherAuth("register", RegisterUsernameBox.Text, password))
                return;
        }
        else if (LoginPasswordBox.Password.Length > 0 &&
                 !PrepareLauncherAuth("login", LoginUsernameBox.Text,
                     LoginPasswordBox.Password))
            return;

        LaunchMta();
        }
        finally
        {
            PlayButton.IsEnabled = true;
            _launchingAfterUpdates = false;
        }
    }

    private async Task<bool> ClientReadyToPlayAsync()
    {
        // Keep legacy local developer flow intact when no project client is configured.
        if (string.IsNullOrWhiteSpace(_gameInstallPath)) return true;
        var ready = await EnsureClientUpdatedAsync();
        if (!ready) StatusText.Text =
            "Игровой клиент не готов. Исправь ошибку проверки файлов перед входом.";
        return ready;
    }

    private Task<bool> EnsureClientUpdatedAsync()
    {
        // Reuse the currently running check when Play is pressed during startup scan.
        if (_clientUpdateInFlight is { IsCompleted: false })
            return _clientUpdateInFlight;
        _clientUpdateInFlight = CheckAndInstallClientUpdatesAsync();
        return _clientUpdateInFlight;
    }

    private async Task<bool> CheckAndInstallClientUpdatesAsync()
    {
        var root = _gameInstallPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            ClientUpdateStatusText.Text = "Сначала выбери папку игры в настройках.";
            return false;
        }

        UpdateCheckButton.IsEnabled = false;
        ClientUpdateProgressBar.Value = 0;
        ClientUpdateStatusText.Text = "Проверяем файлы игрового клиента...";
        try
        {
            // Licensed GTA files are repaired only from the user's own installation.
            var gta = GameClientInstaller.GtaDirectory(root);
            if (!File.Exists(Path.Combine(gta, "gta_sa.exe")) ||
                !File.Exists(Path.Combine(gta, "models", "gta3.img")))
            {
                if (!Directory.Exists(_gameSourcePath))
                {
                    ClientUpdateStatusText.Text =
                        "Не хватает GTA SA. Укажи исходную GTA в разделе «Установка игры».";
                    return false;
                }

                ClientUpdateStatusText.Text = "Восстанавливаем GTA из твоей установки...";
                var gameProgress = new Progress<GameClientInstaller.InstallProgress>(p =>
                {
                    ClientUpdateProgressBar.Value = p.Percent;
                    ClientUpdateStatusText.Text = p.Message;
                });
                await GameClientInstaller.CopyOwnedGameAsync(
                    _gameSourcePath, root, gameProgress, CancellationToken.None);
                ClientUpdateProgressBar.Value = 0;
            }

            var progress = new Progress<ProjectUpdateService.ProgressInfo>(p =>
            {
                ClientUpdateProgressBar.Value = p.Percent;
                ClientUpdateStatusText.Text = p.Message;
            });
            var result = await ProjectUpdateService.CheckAndRepairAsync(
                root, progress, CancellationToken.None);
            ClientUpdateStatusText.Text = result.DownloadedFiles == 0
                ? $"Все файлы актуальны • версия {result.Version}"
                : $"Установлено недостающих/обновлённых файлов: {result.DownloadedFiles} • версия {result.Version}";
            ClientUpdateProgressBar.Value = 100;

            if (!GameClientInstaller.HasLocalMta(root))
            {
                ClientUpdateStatusText.Text =
                    "Файлы проекта готовы, но MTA не установлен. Открой настройки.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ClientUpdateStatusText.Text = "Ошибка проверки: " + ex.Message;
            return false;
        }
        finally
        {
            UpdateCheckButton.IsEnabled = true;
        }
    }

    private async void UpdateCheckButton_Click(object sender, RoutedEventArgs e)
    {
        ClientUpdateStatusText.Text = "Проверка файлов временно отключена.";
    }

    private void LaunchMta()
    {
        try
        {
            SaveSettings();
            // The selected MTA is used exclusively; no file repair, checksum,
            // installation scan, or alternate-client fallback is performed.
            // The native MTA integrity checks remain enabled and unchanged.
            StatusText.Text = !string.IsNullOrWhiteSpace(_mtaFolder)
                ? MtaClientLauncher.StartSelected(_mtaFolder)
                : MtaClientLauncher.Start(null, null);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось запустить MTA: " + ex.Message;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void NewsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboard();
        StatusText.Text = "Новости проекта отображаются на главной.";
    }
    private void PromoButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboard();
        StatusText.Text = "Промокоды будут доступны после подключения серверного API.";
    }
    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboard();
        RightTabs.SelectedIndex = 0;
        StatusText.Text = "Введи данные для входа и нажми «ИГРАТЬ».";
    }
    private void ShowDashboard()
    {
        SettingsView.Visibility = Visibility.Collapsed;
        DashboardView.Visibility = Visibility.Visible;
        HomeNavButton.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0, 125, 137));
        SettingsNavButton.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(20, 43, 56));
        HomeNavButton.Effect = FindResource("NeonGlow") as System.Windows.Media.Effects.Effect;
        SettingsNavButton.Effect = null;
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) =>
        ShowDashboard();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        SettingsNavButton.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0, 125, 137));
        HomeNavButton.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(20, 43, 56));
        SettingsNavButton.Effect =
            FindResource("NeonGlow") as System.Windows.Media.Effects.Effect;
        HomeNavButton.Effect = null;

        // Discard any unsaved UI edits when returning to the settings tab.
        MtaFolderBox.Text = string.IsNullOrWhiteSpace(_mtaFolder)
            ? "Папка MTA пока не выбрана" : _mtaFolder;
        InstallPathBox.Text = string.IsNullOrWhiteSpace(_gameInstallPath)
            ? "Папка пока не выбрана" : _gameInstallPath;
        SourceGtaBox.Text = string.IsNullOrWhiteSpace(_gameSourcePath)
            ? "Исходная GTA SA не выбрана" : _gameSourcePath;
        UpdateInstallOption.IsChecked = _gameInstallMode == "update";
        FullInstallOption.IsChecked = _gameInstallMode != "update";
        UpdateClientFolderPreview();
    }

    private void BrowseMtaFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выбери папку MTA, в которой находится Multi Theft Auto.exe",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_mtaFolder) ? _mtaFolder : ""
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var candidate = Path.Combine(dialog.SelectedPath, "Multi Theft Auto.exe");
        if (!File.Exists(candidate))
        {
            InstallStatusText.Text =
                "Не найден Multi Theft Auto.exe. Выбери папку установленного MTA.";
            return;
        }

        MtaFolderBox.Text = dialog.SelectedPath;
        InstallStatusText.Text = "Клиент выбран. Нажми «СОХРАНИТЬ».";
    }

    private void BrowseInstallFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выбери папку для установки файлов MARCH ROLE PLAY",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(InstallPathBox.Text)
                ? InstallPathBox.Text : ""
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            InstallPathBox.Text = dialog.SelectedPath;
            UpdateClientFolderPreview();
        }
    }

    private void UpdateClientFolderPreview()
    {
        var root = InstallPathBox.Text;
        if (string.IsNullOrWhiteSpace(root) || root == "Папка пока не выбрана")
        {
            GtaLocationText.Text = "GTA San Andreas — выбери папку проекта";
            MtaLocationText.Text = "MTA San Andreas — выбери папку проекта";
            MtaInstallStatusText.Text = "MTA ещё не установлен в папку проекта";
            return;
        }

        try
        {
            GtaLocationText.Text = GameClientInstaller.GtaDirectory(root);
            MtaLocationText.Text = GameClientInstaller.MtaDirectory(root);
            MtaInstallStatusText.Text = GameClientInstaller.HasLocalMta(root)
                ? "Клиент MTA обнаружен в папке проекта"
                : "Установи официальный MTA в указанную папку";
        }
        catch
        {
            GtaLocationText.Text = "Недопустимая папка клиента";
            MtaLocationText.Text = "Недопустимая папка клиента";
            MtaInstallStatusText.Text = "Выбери другую папку";
        }
    }

    private void DownloadOfficialMta_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://multitheftauto.com/")
            {
                UseShellExecute = true
            });
            InstallStatusText.Text =
                "Установи MTA в папку MTA San Andreas и укажи GTA San Andreas из сборки.";
        }
        catch (Exception ex)
        {
            InstallStatusText.Text = "Не удалось открыть сайт MTA: " + ex.Message;
        }
    }

    private void CopyMtaPaths_Click(object sender, RoutedEventArgs e)
    {
        var root = InstallPathBox.Text;
        if (string.IsNullOrWhiteSpace(root) || root == "Папка пока не выбрана")
        {
            InstallStatusText.Text = "Сначала выбери папку MARCH ROLE PLAY.";
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText("Папка установки MTA: " +
                GameClientInstaller.MtaDirectory(root) + Environment.NewLine +
                "Папка GTA San Andreas: " +
                GameClientInstaller.GtaDirectory(root));
            InstallStatusText.Text = "Оба пути для установщика MTA скопированы.";
        }
        catch (Exception ex)
        {
            InstallStatusText.Text = "Не удалось скопировать пути: " + ex.Message;
        }
    }

    private void BrowseGtaSource_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выбери свою установленную GTA San Andreas",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(SourceGtaBox.Text)
                ? SourceGtaBox.Text : ""
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SourceGtaBox.Text = dialog.SelectedPath;
    }

    private async void InstallGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installerCancellation != null)
        {
            _installerCancellation.Cancel();
            return;
        }

        var target = InstallPathBox.Text;
        var source = SourceGtaBox.Text;
        var update = UpdateInstallOption.IsChecked == true;
        if (string.IsNullOrWhiteSpace(target) || target == "Папка пока не выбрана")
        {
            InstallStatusText.Text = "Сначала выбери папку для нового игрового клиента.";
            return;
        }
        if (!update && (string.IsNullOrWhiteSpace(source) ||
                        source == "Исходная GTA SA не выбрана"))
        {
            InstallStatusText.Text = "Для полной установки выбери свою GTA San Andreas.";
            return;
        }

        _gameInstallPath = target;
        _gameSourcePath = source == "Исходная GTA SA не выбрана" ? "" : source;
        _gameInstallMode = update ? "update" : "full";

        try { SaveSettings(); }
        catch (Exception ex)
        {
            InstallStatusText.Text = "Ошибка сохранения настроек: " + ex.Message;
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _installerCancellation = cancellation;
        InstallGameButton.Content = "ОТМЕНА";
        InstallProgressBar.Value = 0;
        InstallStatusText.Text = "Подготовка...";
        var progress = new Progress<GameClientInstaller.InstallProgress>(p =>
        {
            InstallProgressBar.Value = p.Percent;
            InstallStatusText.Text = p.Message;
        });

        try
        {
            if (update)
            {
                var assetProgress = new Progress<ProjectUpdateService.ProgressInfo>(p =>
                {
                    InstallProgressBar.Value = p.Percent;
                    InstallStatusText.Text = p.Message;
                    ClientUpdateStatusText.Text = p.Message;
                    ClientUpdateProgressBar.Value = p.Percent;
                });
                var result = await ProjectUpdateService.CheckAndRepairAsync(
                    target, assetProgress, cancellation.Token);
                InstallStatusText.Text = $"Проверено {result.TotalFiles} файлов. " +
                    $"Установлено {result.DownloadedFiles}. Версия {result.Version}.";
                ClientUpdateStatusText.Text = InstallStatusText.Text;
            }
            else
            {
                await GameClientInstaller.CopyOwnedGameAsync(source, target, progress,
                    cancellation.Token);
                InstallStatusText.Text =
                    "GTA SA готова. MTA устанавливается из официального источника.";
            }
            UpdateClientFolderPreview();
        }
        catch (OperationCanceledException)
        {
            InstallStatusText.Text = "Операция отменена. Существующие файлы сохранены.";
        }
        catch (Exception ex)
        {
            InstallStatusText.Text = "Ошибка установки: " + ex.Message;
        }
        finally
        {
            _installerCancellation = null;
            InstallGameButton.Content = "НАЧАТЬ";
        }
    }

    private void SettingsSave_Click(object sender, RoutedEventArgs e)
    {
        var selection = MtaFolderBox.Text.Trim();
        if (selection == "Папка MTA пока не выбрана")
            selection = "";

        if (!string.IsNullOrWhiteSpace(selection) &&
            !File.Exists(Path.Combine(selection, "Multi Theft Auto.exe")))
        {
            InstallStatusText.Text =
                "Папка не содержит Multi Theft Auto.exe. Выбери установленный MTA.";
            return;
        }

        var previous = _mtaFolder;
        try
        {
            _mtaFolder = selection;
            SaveSettings();
            StatusText.Text = string.IsNullOrWhiteSpace(selection)
                ? "Используется автоматический поиск MTA. Проверка файлов отключена."
                : "Выбран клиент MTA: " + selection;
            ShowDashboard();
        }
        catch (Exception ex)
        {
            _mtaFolder = previous;
            InstallStatusText.Text = "Не удалось сохранить папку MTA: " + ex.Message;
        }
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e) =>
        ShowDashboard();

    private sealed record LauncherSettings(
        string MtaPath, string Host, int Port,
        string? DiscordAppId = null,
        string? TelegramUrl = null,
        string? VkUrl = null,
        string? DiscordInviteUrl = null,
        string? Username = null,
        bool RememberUsername = false,
        string? AuthResourcePath = null,
        string? GameInstallPath = null,
        string? GameInstallMode = null,
        string? GameSourcePath = null);
}