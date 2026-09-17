# Changelog

## 2.4.1

### WindowsGSM field synchronization

- Fixed password handling so an absent `-password` parameter no longer clears an existing password.
- When no password parameter is supplied, preserves `Password` and normalizes `IsPasswordProtected` to match it.
- Multiple password parameters are handled deterministically; the last value wins.
- Removed the old destructive default `-password ""` from new WindowsGSM configs.
- `Server IP Address` now synchronizes to `P2pProxyAddress`.
- `Server Port` now always synchronizes to `DirectConnectionServerPort` without automatically enabling Direct IP.
- `Server Maxplayer` synchronizes to `MaxPlayerCount` with validation/logging.
- `Server Name` synchronization retained and instrumented.
- `Server Query Port` is intentionally not mapped because current Windrose exposes no Query/A2S port in `ServerDescription.json`.
- `Server GSLT` is intentionally ignored because Windrose does not use Steam GSLT.

### Connection modes

- Added preferred `-wr-connection invite|direct` parameter.
- Kept `-wr-direct true|false` as a backward-compatible alias.
- New/incomplete configurations initialize to Invite Code / Connection Service / ICE-P2P (`UseDirectConnection=false`).
- Existing `UseDirectConnection` is preserved when no connection-mode parameter is supplied.
- Unrelated WindowsGSM edits no longer reset manually selected Direct IP mode.
- Added clear runtime logs for effective connection mode and Connection Service region.
- Documented Invite Code/P2P and Direct IP as alternative modes; simultaneous dual-mode operation is not documented by Windrose and was not observed in the tested build.
- Documented the official same-LAN Invite Code limitation and Direct IP workaround.

### Safe STOP / shutdown

- Fixed STOP logging when WindowsGSM creates the plugin instance without `ServerConfig`.
- STOP logging now resolves the server root from the running process.
- Added native Windows console shutdown using `AttachConsole` + `GenerateConsoleCtrlEvent(CTRL_C_EVENT)`.
- `CloseMainWindow` is retained as a compatibility fallback.
- `Process.Kill()` is used only as the final fallback when graceful methods fail/time out.
- Validated three consecutive native-console STOP cycles without requiring the forced Kill fallback.
- Improved process-exit diagnostics: non-zero exit codes are no longer labeled as a normal zero exit.
- Documented observed Windrose build `0.10.0.9.32-22d39a16` behavior where Windows returned `0xC0000005` after synchronous backups, RocksDB closure, engine shutdown and log closure had already completed.

### Documentation

- Added detailed connection-mode documentation in English and Brazilian Portuguese.
- Added detailed STOP/shutdown documentation in English and Brazilian Portuguese.
- Added WindowsGSM field-mapping explanations.
- Added Admin/RCON note clarifying that current official Windrose documentation does not expose native administrator/RCON configuration.
- Updated legacy migration documentation for v2.4.1.

## 2.4.0

- WindowsGSM `Server Start Map` now synchronizes to Windrose `WorldDescription.WorldName`.
- Changing the map name keeps the same `WorldIslandId`, `islandId`, and physical save directory.
- Added automatic `R5WorldDescriptionUpdater.exe` application for `WorldName` changes on `RocksDB_v2`.
- Added `WorldDescription.json` backup before friendly-name synchronization.
- Added rollback attempt when the world-description updater fails.
- Blank WindowsGSM map names leave the existing Windrose `WorldName` unchanged.
- Existing World Guard identity protections remain unchanged.

## 2.3.0

- Fixed current Windrose `WorldDescription.json` identity key handling (`islandId`, lowercase `i`).
- Retained compatibility fallback for `IslandId`.
- Fixed false `IslandId is empty` errors on current Windrose worlds.
- Added guarded repair for genuinely empty/missing `islandId`.
- Runs `R5WorldDescriptionUpdater.exe` after plugin-owned `islandId` repair on `RocksDB_v2`.
- Fixed locked-world recovery when `ServerDescription.json` has an empty `WorldIslandId`.
- Excluded backup databases from automatic active-world selection.
- Deduplicated identical World IDs across version directories.

## 2.2.0

- Added automatic adoption of the only existing world after first bootstrap.
- Added missing `WorldIslandId` restoration from locked state.

## 2.1.0

- Decoupled WindowsGSM Server Start Map from Windrose `WorldIslandId`.
- Allowed incomplete clean bootstrap when no save exists.
