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
5. The launcher tries to find the local server's running process and `march_auth` resource directory automatically. If it cannot, in the launcher **Настройки → ВЫБРАТЬ ПАПКУ march_auth**, select the existing folder containing `meta.xml`, and retry.

### Debugging SSO

The launcher shows an explicit folder-not-found, invalid-credentials, or no-response result. A pending ticket file named `launcher_sso_pending.json` and server response `launcher_sso_result.json` are temporary and must never be listed in `meta.xml` or shared publicly. For more MTA diagnostics use `/debugscript 3` and server logs. Existing manual game login remains available if no SSO ticket was prepared.

### Security scope

This is **local testing only**, NOT production/public-server authentication. A ticket briefly contains the entered password in an on-disk file (up to 2 minutes), is removed by MTA on consumption, and is not saved in launcher settings. Closing or crashing MTA before consumption can leave the file until its lifetime expires and the server reads it; handle the server resource folder as sensitive. The ticket is never sent to remote clients in `meta.xml`; only an MTA player connecting from loopback is eligible. For users connecting to another computer, replace this with a server-managed **HTTPS session API**, strict per-user binding, replay protection, and short-lived tokens.

## Discord Rich Presence

The launcher is hardcoded to use the project Discord Application ID `1301565059113418763`. Invite button: https://discord.gg/GMkC5YGXku. With desktop Discord running and activity privacy enabled, the launcher publishes its presence and real MTA server online count every 20 seconds when available. Discord app ID and invite are **not editable** in launcher settings.

## Community buttons

The sidebar includes Telegram, VK and Discord vector icons. Discord uses the project invite. Previously saved VK/Telegram URLs may still be read from older local settings, but there is no editable social-link configuration at present.
