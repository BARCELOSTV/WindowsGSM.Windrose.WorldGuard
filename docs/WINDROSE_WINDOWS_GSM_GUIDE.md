# WindowsGSM Windrose WorldGuard — Complete Guide

**Plugin:** `WindowsGSM.Windrose.WorldGuard`  
**Version:** `2.4.1`  
**Game:** Windrose  
**Dedicated Server Steam AppID:** `4129620`  
**Platform:** Windows

## 1. Purpose

WorldGuard manages a Windrose Dedicated Server through WindowsGSM while protecting the persistent identity of the active world. It is designed to prevent normal restarts, configuration changes, updates, migration mistakes, or incomplete first-start metadata from causing an unintended replacement world.

Protected identity:

```text
PersistentServerId
InviteCode
WorldIslandId
WorldDescription.json -> islandId
Worlds\<WORLD_ID>\
```

Runtime identity lock:

```text
<serverfiles>\WindowsGSM_Windrose_State.json
```

Diagnostics:

```text
<serverfiles>\WindowsGSM_Windrose.log
```

If identity is inconsistent or ambiguous, WorldGuard blocks startup instead of guessing.

## 2. Installation / update

Copy the complete `Windrose.cs` directory into:

```text
<WindowsGSM>\plugins\
```

Expected layout:

```text
<WindowsGSM>\plugins\Windrose.cs\Windrose.cs
<WindowsGSM>\plugins\Windrose.cs\Windrose.png
<WindowsGSM>\plugins\Windrose.cs\author.png
```

Restart WindowsGSM or reload plugins.

The plugin installs/updates the official dedicated server through SteamCMD using AppID `4129620` and starts:

```text
R5\Binaries\Win64\WindroseServer-Win64-Shipping.exe
```

## 3. First clean launch

A truly new server may have no `ServerDescription.json`, world, or WorldGuard state. WorldGuard allows this first bootstrap so Windrose can create its identity/world.

If exactly one valid active world exists afterward and identity is unambiguous, WorldGuard can adopt and lock it. If multiple worlds exist with no authoritative selection, startup is blocked.

For a new/incomplete connection configuration, `UseDirectConnection` is initialized to `false` (Invite Code / Connection Service mode).

## 4. WindowsGSM field mapping

### Server Name

```text
WindowsGSM Server Name -> ServerDescription_Persistent.ServerName
```

### Server IP Address

```text
WindowsGSM Server IP -> P2pProxyAddress
```

The value is validated as an IP address before being applied.

### Server Port

```text
WindowsGSM Server Port -> DirectConnectionServerPort
```

The value is stored whether Direct IP is currently enabled or not. This does not enable Direct IP by itself.

For Direct IP, the selected port should be reachable in both TCP and UDP.

### Server Query Port

Not mapped. Current Windrose `ServerDescription.json` does not expose a traditional Query/A2S port field.

### Server Maxplayer

```text
WindowsGSM Server Maxplayer -> MaxPlayerCount
```

WorldGuard validates the configured player count before writing it.

### Server Start Map

Used only as the friendly Windrose world name:

```text
WindowsGSM Server Start Map -> WorldDescription.WorldName
```

Example:

```text
Server Start Map = DELTA
```

becomes:

```json
"WorldName": "DELTA"
```

It never changes:

```text
WorldIslandId
islandId
PersistentServerId
InviteCode
Worlds\<WORLD_ID>\ directory name
```

On `RocksDB_v2`, the plugin backs up `WorldDescription.json`, edits the friendly name and runs `R5WorldDescriptionUpdater.exe`. If the updater fails, startup is blocked and rollback is attempted.

### Server GSLT

Ignored. Current Windrose dedicated-server configuration does not use Steam GSLT.

## 5. Password

Explicit password:

```text
-password "MyPassword"
```

Remove password:

```text
-password ""
```

If `-password` is absent, the existing `Password` value is preserved and `IsPasswordProtected` is normalized to match whether that stored password is empty/non-empty.

If multiple password parameters are present, the last occurrence is used. The actual password is never written to the WorldGuard log.

## 6. Connection modes

Windrose exposes one selector:

```text
UseDirectConnection=false -> Invite Code / Connection Service / ICE-P2P
UseDirectConnection=true  -> Direct IP
```

Preferred explicit options:

```text
-wr-connection invite
-wr-connection direct
```

Backward-compatible aliases:

```text
-wr-direct false
-wr-direct true
```

### Preservation rule

If neither parameter is supplied and `UseDirectConnection` already exists as a valid boolean, WorldGuard leaves it unchanged.

Therefore changing Server Name, IP, Port, Max Players or WorldName does not switch the networking mode.

Only a missing/invalid connection field is initialized to `false`.

### Invite Code / ICE-P2P

With `UseDirectConnection=false`, Windrose uses its Connection Service/ICE-P2P flow. The server registers with the Connection Manager and clients locate it by Invite Code.

Region controls:

```text
-wr-region AUTO
-wr-region EU
-wr-region SEA
-wr-region CIS
```

The official Windrose FAQ documents a same-LAN Invite Code limitation; Direct IP is the practical workaround for that scenario.

### Direct IP

Use:

```text
-wr-connection direct
```

Connect using:

```text
<IP>:<Server Port>
```

The port should be reachable in TCP and UDP.

### Simultaneous modes

The official configuration exposes one `UseDirectConnection` boolean and official documentation presents Direct IP as an alternative connection method rather than a documented simultaneous dual-mode setup.

In real tests on Windrose build `0.10.0.9.32-22d39a16`:

- Invite mode registered successfully with the Connection Manager and accepted a real ICE/P2P client connection.
- Direct mode created direct sockets and accepted a real client connection by IP + port.
- The direct-mode session was not discoverable through Invite Code.
- Returning to Invite mode restored Connection Manager discovery.

This behavior should not be interpreted as a WorldGuard failure.

See `CONNECTION_MODES_EN.txt` / `CONNECTION_MODES_PT-BR.txt` for more detail.

## 7. Other parameters

Optional direct bind/proxy address:

```text
-wr-bind 0.0.0.0
```

Broken-save recovery field:

```text
-wr-autorestore true|false
```

Intentional switch to another already-existing world:

```text
-wr-adopt-world <WORLD_ID>
```

WorldGuard verifies that the requested world exists before changing the lock. Remove `-wr-adopt-world` after the intentional switch succeeds.

## 8. ServerDescription.json

Location:

```text
<serverfiles>\R5\ServerDescription.json
```

Critical persistent fields:

```text
PersistentServerId
InviteCode
WorldIslandId
```

WorldGuard patches administrator-facing values in the existing JSON object, preserving unknown/new fields instead of rebuilding the file from a rigid schema.

Do not casually edit identity fields manually.

## 9. Save locations

Current database:

```text
R5\Saved\SaveProfiles\Default\RocksDB_v2\
```

Legacy database:

```text
R5\Saved\SaveProfiles\Default\RocksDB\
```

Typical world:

```text
<DB_ROOT>\<GAME_VERSION>\Worlds\<WORLD_ID>\WorldDescription.json
```

WorldGuard searches current `RocksDB_v2` first and then legacy `RocksDB`. Backup directories such as `RocksDB_v2_Backups` are deliberately excluded from automatic active-world selection.

## 10. WorldDescription identity

Current Windrose uses:

```json
"islandId": "<WORLD_ID>"
```

WorldGuard reads lowercase `islandId` first and keeps a compatibility fallback for `IslandId`.

These should agree:

```text
ServerDescription.json -> WorldIslandId
WorldDescription.json -> islandId
Worlds\<WORLD_ID>\ directory name
```

A genuinely empty `islandId` can be repaired from the folder name only when the identity is already unambiguous. On `RocksDB_v2`, the updater is then executed.

## 11. R5WorldDescriptionUpdater.exe

WorldGuard searches known locations including:

```text
<serverfiles>\R5WorldDescriptionUpdater.exe
<serverfiles>\R5\R5WorldDescriptionUpdater.exe
<serverfiles>\R5\Binaries\Win64\R5WorldDescriptionUpdater.exe
```

If an updater-required edit cannot be applied safely, startup is blocked rather than continuing with a half-applied database change.

## 12. Safe STOP / shutdown

WindowsGSM `v1.23.1` may launch Windrose without a usable `MainWindowHandle`. WorldGuard v2.4.1 therefore uses the native Windows Console API as the primary STOP mechanism:

```text
AttachConsole(PID)
-> ignore CTRL+C in WindowsGSM sender
-> GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)
-> detach
-> wait up to 20 seconds
-> CloseMainWindow compatibility fallback
-> Process.Kill() FINAL fallback only
```

Testing with Windrose build `0.10.0.9.32-22d39a16` confirmed that the console signal causes Windrose to request `ConsoleCtrl RequestExit`, perform synchronous backups, close RocksDB, shut down its engine and close the log. Three consecutive WindowsGSM STOP cycles completed without the forced Kill fallback.

### Tested non-zero process exit

The tested build returned:

```text
-1073741819 (0xC0000005)
```

after the internal shutdown sequence had completed. WorldGuard now logs this accurately as a non-zero process exit warning; it does not call it a normal zero exit.

A non-zero code alone is not proof of database integrity. If seen on another Windrose build, review `R5.log` for backup completion, RocksDB closure, engine shutdown and log closure.

See `STOP_BEHAVIOR_EN.txt` / `STOP_BEHAVIOR_PT-BR.txt`.

## 13. Legacy migration

Full guides:

- `WINDROSE_LEGACY_MIGRATION_GUIDE_EN.txt`
- `WINDROSE_LEGACY_MIGRATION_GUIDE_PT-BR.txt`

At minimum preserve:

```text
R5\ServerDescription.json
R5\Saved\
```

Recommended procedure:

1. Stop the old server completely.
2. Stop the WindowsGSM Windrose instance.
3. Verify no Windrose server process is running.
4. Back up the old server.
5. Install Windrose through WindowsGSM without bootstrapping a replacement world.
6. Copy the old `R5\Saved\` into the WindowsGSM server.
7. Copy the original `R5\ServerDescription.json`.
8. If this WindowsGSM instance belonged to another server identity, remove only its old `WindowsGSM_Windrose_State.json` as part of the deliberate migration.
9. Start through WindowsGSM.
10. Verify the expected world/progress.
11. Stop and start again to confirm persistence.

## 14. Runtime files / backups

WorldGuard state:

```text
<serverfiles>\WindowsGSM_Windrose_State.json
```

Plugin log:

```text
<serverfiles>\WindowsGSM_Windrose.log
```

Identity/configuration backups:

```text
<serverfiles>\WindowsGSM_Windrose_Backups\
```

These are not a replacement for a full backup of `R5\Saved\`.

## 15. Troubleshooting

### WorldIslandId empty / missing

A clean first bootstrap with exactly one valid world can be adopted safely. If multiple worlds exist, use an explicit `-wr-adopt-world <WORLD_ID>` rather than guessing.

### Configured world not found

Check active `RocksDB_v2` / `RocksDB` locations and verify the configured World ID. Do not repeatedly start Windrose manually while identity is unresolved.

### islandId mismatch

The physical world folder and `WorldDescription.json -> islandId` should match. A conflicting non-empty identity blocks startup.

### Password unexpectedly disabled

Check whether the stored `Password` is empty and whether `-password ""` was explicitly supplied. Without a password parameter, WorldGuard preserves the stored password and normalizes `IsPasswordProtected`.

### Direct IP unexpectedly changed back to Invite mode

Version 2.4.1 preserves an existing `UseDirectConnection` value when no connection parameter is present. Check `WindowsGSM_Windrose.log` for the effective mode and remove any unintended `-wr-connection` / `-wr-direct` override.

### Invite Code does not work while Direct IP is enabled

Current Windrose configuration treats `UseDirectConnection` as a mode selector; simultaneous dual-mode operation is not documented. Switch explicitly to:

```text
-wr-connection invite
```

or set/preserve `UseDirectConnection=false`.

### STOP returns 0xC0000005 on tested Windrose build

Review `R5.log`. On tested build `0.10.0.9.32-22d39a16`, the code occurred after successful synchronous backups, RocksDB closure, engine shutdown and log closure. WorldGuard logs a warning instead of treating the non-zero code as a normal zero exit.

### STOP uses forced Kill fallback

Inspect `WindowsGSM_Windrose.log`. A healthy tested v2.4.1 setup successfully attached to the Windrose console and sent `CTRL_C_EVENT`. If AttachConsole fails or Windrose does not exit within the timeout, investigate permissions/process topology before assuming the forced fallback is safe.

### R5WorldDescriptionUpdater missing/fails

Run SteamCMD Update/Validate and verify the updater is present. WorldGuard blocks updater-required changes when they cannot be applied safely.

## 16. Admin / RCON

**Admin / RCON:** The official Windrose Dedicated Server, in its currently documented version, does not provide a documented native administrator or RCON configuration. Features of this type require third-party tools or mods.

## 17. Safety rules

- Stop the server before manually copying/editing save data.
- Keep full backups of `R5\Saved\` and `R5\ServerDescription.json`.
- Do not rename `Worlds\<WORLD_ID>` directories without a migration plan.
- Do not casually modify `PersistentServerId`, `WorldIslandId`, or `islandId`.
- Do not run multiple server processes against the same save database.
- Do not delete WorldGuard state just to bypass an identity error.

## 18. Tested environment

Validation for v2.4.1 included:

```text
WindowsGSM: v1.23.1
Windrose Dedicated Server build: 0.10.0.9.32-22d39a16
```

Validated behaviors included clean bootstrap, repeated restarts, world identity persistence, Max Players changes, password persistence/normalization, friendly WorldName synchronization, Server IP/Port synchronization, Invite Code/ICE-P2P connection, Direct IP connection, connection-mode preservation, mode switching, and repeated native-console STOP cycles.

Windrose is actively developed. Future server/save/network behavior may change, so major Windrose updates should be checked against current dedicated-server documentation.

## 19. Author

**BARCELOSTV / Luiz Augusto Barcelos**

- YouTube: https://www.youtube.com/@BARCELOSTV_OFICIAL/videos
- Twitch: https://www.twitch.tv/barcelostv
- Steam: https://steamcommunity.com/id/BARCELOSTV/

## 20. License

The plugin source/documentation are distributed under the MIT License included in the repository. Third-party names, trademarks, logos, game assets, and proprietary software remain the property of their respective owners.
