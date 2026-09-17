# WindowsGSM.Windrose.WorldGuard

WindowsGSM plugin for the **Windrose Dedicated Server** with persistent world protection, legacy migration support, WindowsGSM configuration synchronization, connection-mode management, and safe shutdown handling.

**Current version:** `2.4.1`  
**Windrose Dedicated Server AppID:** `4129620`  
**Platform:** Windows  
**Validated with:** WindowsGSM `v1.23.1` and Windrose server build `0.10.0.9.32-22d39a16`

<p align="center">
  <img src="Windrose.cs/Windrose.png" width="128" alt="Windrose Dedicated Server" />
</p>

## Why WorldGuard?

Windrose ties the active world to persistent identity values stored in `R5\ServerDescription.json` and the world database. If those values become inconsistent, an administrator can accidentally start a different/replacement world.

WorldGuard validates the identity before startup and protects:

- `PersistentServerId`
- `InviteCode`
- `WorldIslandId`
- `WorldDescription.json -> islandId`
- the physical `Worlds\<WORLD_ID>\` directory

If the identity is unsafe or ambiguous, the plugin blocks startup instead of guessing.

## Features

- SteamCMD install/update support (`AppID 4129620`)
- Protection against accidental world regeneration
- Automatic first-world adoption when exactly one valid world is available
- Locked-world recovery when `WorldIslandId` is missing but the protected identity is unambiguous
- Current `RocksDB_v2` and legacy `RocksDB` support
- Legacy dedicated-server migration support
- Current lowercase `islandId` support with compatibility fallback for `IslandId`
- Safe `Server Start Map` -> `WorldDescription.WorldName` synchronization
- `R5WorldDescriptionUpdater.exe` integration for `RocksDB_v2` world-description changes
- Server Name, IP, Port, Max Players and password synchronization
- Invite Code / ICE-P2P and Direct IP connection modes
- Connection-mode preservation when unrelated WindowsGSM settings are changed
- Native-console graceful STOP through WindowsGSM
- Automatic identity/configuration backups and detailed diagnostics
- MIT licensed

## Installation

Copy the complete [`Windrose.cs`](Windrose.cs/) folder into:

```text
<WindowsGSM>\plugins\
```

Final layout:

```text
<WindowsGSM>\plugins\Windrose.cs\Windrose.cs
<WindowsGSM>\plugins\Windrose.cs\Windrose.png
<WindowsGSM>\plugins\Windrose.cs\author.png
```

Restart WindowsGSM or use **Reload Plugins**.

See [INSTALL.md](INSTALL.md) for the installation checklist.

## WindowsGSM field mapping

| WindowsGSM field | Windrose behavior |
| --- | --- |
| Server Name | `ServerDescription_Persistent.ServerName` |
| Server IP Address | `P2pProxyAddress` |
| Server Port | `DirectConnectionServerPort` |
| Server Query Port | Intentionally not mapped; current Windrose has no Query/A2S field in `ServerDescription.json` |
| Server Maxplayer | `MaxPlayerCount` |
| Server Start Map | Friendly `WorldDescription.WorldName` only |
| Server GSLT | Ignored; Windrose does not use Steam GSLT |
| Server Start Param | WorldGuard options listed below |

Changing **Server Start Map** never changes `WorldIslandId`, `islandId`, or the physical save directory.

## Connection modes

Windrose exposes one `UseDirectConnection` selector:

```text
false -> Invite Code / Connection Service / ICE-P2P
true  -> Direct IP
```

For a new/incomplete configuration, WorldGuard initializes:

```json
"UseDirectConnection": false
```

so **Invite Code mode is the initial default**.

After the value exists, WorldGuard preserves it unless the administrator explicitly overrides it. Therefore changing Max Players, Server Name, IP, Port or World Name does not silently change the selected networking mode.

Preferred explicit parameters:

```text
-wr-connection invite
-wr-connection direct
```

Backward-compatible aliases:

```text
-wr-direct false
-wr-direct true
```

The official Windrose documentation presents Direct IP as an alternative connection mode and does not document simultaneous dual-mode operation. In tests with build `0.10.0.9.32-22d39a16`, Direct IP worked through `IP:port`, while that same direct-mode session was not discoverable through Invite Code. Switching back to Invite Code mode restored Connection Manager/ICE-P2P discovery.

The official Windrose FAQ also documents a same-LAN Invite Code limitation; Direct IP is the practical workaround for that scenario.

Detailed connection documentation:

- [Connection modes — English](docs/CONNECTION_MODES_EN.txt)
- [Métodos de conexão — Português do Brasil](docs/CONNECTION_MODES_PT-BR.txt)

## Additional Parameters

```text
-password "your password"
-wr-region AUTO|EU|SEA|CIS
-wr-connection invite|direct
-wr-direct true|false
-wr-bind 0.0.0.0
-wr-autorestore true|false
-wr-adopt-world <WORLD_ID>
```

Examples:

```text
-password "MyPassword"
-wr-connection invite
-wr-connection direct
-wr-region EU
```

`-wr-adopt-world` is intended only for an explicit switch to an already existing world. Remove it after the successful switch.

### Password behavior

If `-password` is supplied, it explicitly controls `Password` and `IsPasswordProtected`.

```text
-password "MyPassword" -> protected
-password ""           -> explicitly unprotected
```

If no password parameter is supplied, the existing password is preserved and `IsPasswordProtected` is normalized to match whether the stored password is empty or non-empty.

## Safe STOP / shutdown

WindowsGSM `v1.23.1` can start Windrose without a usable `MainWindowHandle`. Version 2.4.1 therefore uses the native Windows Console API as the primary graceful-stop path:

```text
AttachConsole(PID)
-> CTRL_C_EVENT
-> wait for Windrose to exit
-> CloseMainWindow compatibility fallback
-> Process.Kill() only as FINAL fallback
```

Validated STOP sessions showed Windrose receiving `ConsoleCtrl RequestExit`, completing synchronous backups, closing RocksDB, shutting down the game engine and closing its log before the process ended. The forced `Kill()` fallback was not required.

### Non-zero exit code note

On tested Windrose build `0.10.0.9.32-22d39a16`, Windows reported exit code:

```text
-1073741819 (0xC0000005)
```

after the internal Windrose shutdown sequence had already completed successfully. WorldGuard logs this as a **non-zero process exit warning**, not as a successful zero exit code. If seen on another build, review `R5.log` to verify backup completion and RocksDB/engine shutdown before assuming the stop was clean.

Detailed STOP documentation:

- [STOP behavior — English](docs/STOP_BEHAVIOR_EN.txt)
- [Comportamento do STOP — Português do Brasil](docs/STOP_BEHAVIOR_PT-BR.txt)

## Admin / RCON

**Admin / RCON:** The official Windrose Dedicated Server, in its currently documented version, does not provide a documented native administrator or RCON configuration. Features of this type require third-party tools or mods.

## Legacy server migration

For an existing server, preserve at minimum:

```text
R5\ServerDescription.json
R5\Saved\
```

WorldGuard supports active worlds under both:

```text
R5\Saved\SaveProfiles\Default\RocksDB_v2\
R5\Saved\SaveProfiles\Default\RocksDB\
```

Do not bootstrap a replacement world before copying the old identity/save data when your goal is to preserve an existing server.

Detailed migration guides:

- [English legacy migration guide](docs/WINDROSE_LEGACY_MIGRATION_GUIDE_EN.txt)
- [Português do Brasil — migração de servidor legado](docs/WINDROSE_LEGACY_MIGRATION_GUIDE_PT-BR.txt)

## Full documentation

- [Complete WindowsGSM Windrose WorldGuard Guide](docs/WINDROSE_WINDOWS_GSM_GUIDE.md)
- [Changelog](CHANGELOG.md)

## Runtime files created by WorldGuard

Inside the WindowsGSM server's `serverfiles` directory:

```text
WindowsGSM_Windrose_State.json
WindowsGSM_Windrose.log
WindowsGSM_Windrose_Backups\
```

These protect/document identity and configuration changes. They are not a replacement for full backups of `R5\Saved\`.

## Safety notes

1. Stop the server before manually copying/editing save data.
2. Back up `R5\Saved\` and `R5\ServerDescription.json` before migration or manual recovery.
3. Do not rename `Worlds\<WORLD_ID>` directories casually.
4. Do not casually modify `PersistentServerId`, `WorldIslandId` or `islandId`.
5. Do not run multiple server instances against the same save database.
6. If WorldGuard blocks startup, inspect `WindowsGSM_Windrose.log` before deleting anything.

## Third-party notice

This is an independent community plugin. It is not affiliated with, endorsed by, sponsored by, or officially supported by the developers/publishers of WindowsGSM or Windrose.

WindowsGSM, Windrose, Steam, SteamCMD and other third-party trademarks/assets remain the property of their respective owners.

## Author

**BARCELOSTV / Luiz Augusto Barcelos**

- YouTube: https://www.youtube.com/@BARCELOSTV_OFICIAL/videos
- Twitch: https://www.twitch.tv/barcelostv
- Steam: https://steamcommunity.com/id/BARCELOSTV/

## License

Released under the [MIT License](LICENSE).
