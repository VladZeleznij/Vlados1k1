# MARCH ROLE PLAY Launcher

Real Windows WPF/.NET 8 launcher for MARCH ROLE PLAY.

## Build
Download the artifact **MARCH-ROLE-PLAY-Launcher-win-x64** from the latest successful [GitHub Actions](https://github.com/VladZeleznij/Vlados1k1/actions). Extract the full folder and launch **MARCH ROLE PLAY Launcher.exe**.

The build pipeline generates a multi-resolution neon M icon, loads the launcher photography, and publishes a self-contained win-x64 release. The top Windows chrome is customized to remove the unwanted system border.

## MTA
The **Play** and **Connect** buttons always connect to **127.0.0.1:22003** using the Windows-registered `mtasa://` handler. The local ASE server monitor reads **UDP 22126** and updates every 20 seconds. The MTA folder field is optional.

## Discord Rich Presence
1. Create your own application at https://discord.com/developers/applications.
2. Set its application name to **MARCH ROLE PLAY Launcher** and upload your launcher icon to the application.
3. Copy its numeric **Application ID** from the Discord Developer Portal.
4. In the launcher, open **Настройки** and paste the ID into **Discord Rich Presence → Application ID**, then click **СОХРАНИТЬ НАСТРОЙКИ**.
5. Keep the desktop Discord client running, signed in, and enable displaying game activity under Discord Activity Privacy settings if necessary.

The launcher uses Discord's **local IPC RPC** transport; no bot token, password or Discord user login is required. It shows **В лаунчере MARCH ROLE PLAY**, **Готовится к игре**, and elapsed session time. Without your own Application ID it cannot register an activity under the project name.

## Community social links
In the launcher **Настройки**, enter your project's Telegram (`https://t.me/...`), VK (`https://vk.com/...`) and Discord invite (`https://discord.gg/...`) URLs. Press **СОХРАНИТЬ НАСТРОЙКИ**. The authentic Telegram/VK/Discord buttons in the sidebar open those URLs. No made-up community URLs are included.

## Authentication
The login/registration screens remain disabled until a secure `march_auth` HTTP API exists. The launcher must not connect directly to the server SQLite database.
