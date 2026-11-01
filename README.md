# MARCH ROLE PLAY Launcher

Actual Windows WPF/.NET 8 launcher built into a self-contained `win-x64` release via [GitHub Actions](https://github.com/VladZeleznij/Vlados1k1/actions).

## Launch
Download the successful build artifact **MARCH-ROLE-PLAY-Launcher-win-x64**. Extract the full folder and run **MARCH ROLE PLAY Launcher.exe**. MTA is launched through the registered `mtasa://127.0.0.1:22003` protocol. Online server monitoring uses MTA ASE UDP 22126.

## Login and registration with existing MTA march_auth

The launcher now supports a **local-only**, one-time password handoff to the user's existing `march_auth` authentication resource. This is designed for a Windows PC running BOTH the launcher and the MTA server. The launcher stores the password **only in RAM** for up to five minutes and opens a loopback-only service on `127.0.0.1:28745`. It does not access SQLite or store passwords in settings.

**Required server update:** Install the **`march_auth` v1.5.2 Launcher SSO** patch built specifically against the actual uploaded `march_auth.zip` (meta.xml version 1.5.2). It updates **`march_auth/server/main.lua`**, **`march_auth/client/main.lua`**, **`march_auth/server/spawn.lua`** and **`march_auth/client/spawn.lua`**, uses the *existing* SQLite `march_accounts.db`, passwordHash/passwordVerify with bcrypt, original spawn flow and MTA ACL behavior. Back up the database and existing scripts before replacing the four Lua files only. Do not delete or replace existing `march_accounts.db`. If your currently installed `march_auth` is NEWER than 1.5.2, merge carefully instead of overwriting newer script changes.

Then restart the **`march_auth`** resource, enter credentials in the launcher, click **ВОЙТИ** or **ЗАРЕГИСТРИРОВАТЬСЯ**, then **ИГРАТЬ**. If valid, the game logs in with the same existing account and hides its login panel. Without the server patch the launcher has no way to authenticate against the existing SQLite DB; in-game login remains available.

**Important security limitation:** This prototype requires the launcher and server to be on the **same local computer**. For a public/remote server use a proper server-managed HTTPS authentication endpoint with short-lived one-time game-session exchange and anti-bruteforce protection. Never expose port 28745 over the network.

## Discord Rich Presence

The launcher is hardcoded to use the project Discord Application ID `1301565059113418763`. Invite button: https://discord.gg/GMkC5YGXku. With desktop Discord running and activity privacy enabled, the launcher publishes its presence and real MTA server online count every 20 seconds when available. Discord app ID and invite are **not editable** in launcher settings.

## Community buttons

The sidebar includes Telegram, VK and Discord vector icons. Discord uses the project invite. Previously saved VK/Telegram URLs may still be read from older local settings, but there is no editable social-link configuration at present.
