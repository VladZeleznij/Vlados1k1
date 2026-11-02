# MARCH ROLE PLAY Launcher

Actual Windows WPF/.NET 8 launcher built into a self-contained `win-x64` release via [GitHub Actions](https://github.com/VladZeleznij/Vlados1k1/actions).

## Launch
Download the successful build artifact **MARCH-ROLE-PLAY-Launcher-win-x64**. Extract the full folder and run **MARCH ROLE PLAY Launcher.exe**. MTA is launched through the registered `mtasa://127.0.0.1:22003` protocol. Online server monitoring uses MTA ASE UDP 22126.

## Launcher login and registration through march_auth 1.5.2 (local development)

**New SSO fix v2** eliminates the old HTTP fetchRemote request to localhost, which MTA can block with error 1006. A single-use authentication ticket is instead handed off using short-lived files inside the existing `march_auth` **server resource directory** on the same Windows PC.

1. Install the **MARCH_ROLE_PLAY_Auth_1.5.2_LAUNCHER_SSO_FIX_v2.zip** patch into the *existing* MTA resource `mods/deathmatch/resources/march_auth`; replace only these files: `server/main.lua`, `server/spawn.lua`, `client/main.lua`, `client/spawn.lua`. **Back up `march_auth` first. Preserve `march_accounts.db`, `march_spawns.db` and `meta.xml`.**
2. In the MTA server console run `stop march_auth`, `refresh`, `start march_auth` around installation.
3. Download the latest successful Windows launcher build via [GitHub Actions](https://github.com/VladZeleznij/Vlados1k1/actions).
4. Start the local MTA server on the same PC as the launcher. Enter login and password, click **ВОЙТИ**; the launcher launches MTA and the server validates credentials against the **existing** bcrypt/SQLite accounts (and native MTA accounts for ACL admins). The **ИГРАТЬ** button also prepares filled credentials automatically.
5. The launcher detects the local server's march_auth directory automatically. Previous saved paths are preserved internally; all folder selection controls have been removed.

### Debugging SSO

The launcher shows an explicit folder-not-found, invalid-credentials, or no-response result. A pending ticket file named `launcher_sso_pending.json` and server response `launcher_sso_result.json` are temporary and must never be listed in `meta.xml` or shared publicly. For more MTA diagnostics use `/debugscript 3` and server logs. Existing manual game login remains available if no SSO ticket was prepared.

### Security scope

This is **local testing only**, NOT production/public-server authentication. A ticket briefly contains the entered password in an on-disk file (up to 2 minutes), is removed by MTA on consumption, and is not saved in launcher settings. Closing or crashing MTA before consumption can leave the file until its lifetime expires and the server reads it; handle the server resource folder as sensitive. The ticket is never sent to remote clients in `meta.xml`; only an MTA player connecting from loopback is eligible. For users connecting to another computer, replace this with a server-managed **HTTPS session API**, strict per-user binding, replay protection, and short-lived tokens.

## Discord Rich Presence

The launcher is hardcoded to use the project Discord Application ID `1301565059113418763`. Invite button: https://discord.gg/GMkC5YGXku. With desktop Discord running and activity privacy enabled, the launcher publishes its presence and real MTA server online count every 20 seconds when available. Discord app ID and invite are **not editable** in launcher settings.

## Community buttons

The sidebar includes Telegram, VK and Discord vector icons. Discord uses the project invite. Previously saved VK/Telegram URLs may still be read from older local settings, but there is no editable social-link configuration at present.

## Remembering account credentials

On successful game login or registration, checking **Запомнить логин и пароль** saves the password as a generic credential in the current Windows user's Credential Manager. The app settings JSON only stores the optional username, not the password. Next launch fills both fields automatically. Unchecking the option deletes the stored Windows credential. Older builds did not save passwords: log in once using the new build to activate this feature. The short-lived SSO ticket inside the local MTA server resource is a separate temporary transport and should not be shared publicly.


## Installation settings inside the launcher

The sidebar **Настройки** switches the main launcher content to a full-size **Установка игры** page. **Главная** and **Назад** return to the dashboard; the login and registration forms remain unchanged. The folder browser and **Полная установка / Обновить файлы** choices are working save-only preferences in `launcher-settings.json`. Actual download/install is disabled until the installer backend is implemented. No separate WPF popup is used.

## Discord game-session presence (local MTA server)

Install **MARCH_ROLE_PLAY_Auth_1.5.2_DISCORD_GAME_STATUS_PATCH.zip** over the existing `march_auth/server/main.lua` from the working SSO v2 patch, and restart `march_auth`. Back up this file first and keep all databases and other files untouched.

The server writes `launcher_discord_session.json` with only a server-verified local logged-in player's nickname and a timestamp; it refreshes every five seconds and removes the file on logout. The launcher treats a session as valid only for twenty seconds and sends either:
- **В лаунчере MARCH ROLE PLAY** — if nobody is authenticated locally, together with normal server population.
- **На сервере MARCH ROLE PLAY** — after an authenticated local game login, including the actual nickname and online count.

Discord's desktop activity display must be enabled. This requires a local MTA server and a launcher running on the same computer, and is not designed for public remote players.

## Saved-password restoration

When the player has previously checked **Запомнить логин и пароль** and completed a successful server login, Windows Credential Manager stores a generic credential for the Windows user. The launcher restores it even when an older launcher version did not preserve the JSON checkbox. A normal **Играть** click automatically prepares the SSO ticket using that saved credential, including when the Registration tab is open but unused. Unchecking the remember option deletes the stored credential.
