# WindowsGSM Windrose WorldGuard — Complete Guide

**Plugin:** `WindowsGSM.Windrose.WorldGuard`  
**Version:** `2.4.0`  
**Game:** Windrose  
**Dedicated Server Steam AppID:** `4129620`  
**Platform:** Windows

## Purpose

WorldGuard manages a Windrose Dedicated Server through WindowsGSM while protecting the persistent identity of the active world. Its main goal is to prevent normal restarts, configuration changes, updates, or incomplete first-start metadata from causing Windrose to initialize an unintended replacement world.

The protected identity includes:

```text
PersistentServerId
InviteCode
WorldIslandId
WorldDescription.json -> islandId
Worlds\<WORLD_ID>\
```

WorldGuard stores its own locked identity in:

```text
<WindowsGSM>\servers\<SERVER_ID>\serverfiles\WindowsGSM_Windrose_State.json
```

and writes diagnostics to:

```text
<WindowsGSM>\servers\<SERVER_ID>\serverfiles\WindowsGSM_Windrose.log
```

## Plugin installation

Copy the complete `Windrose.cs` directory into:

```text
<WindowsGSM>\plugins\
```

The resulting structure must be:

```text
<WindowsGSM>\plugins\Windrose.cs\Windrose.cs
<WindowsGSM>\plugins\Windrose.cs\Windrose.png
<WindowsGSM>\plugins\Windrose.cs\author.png
```

Restart WindowsGSM or reload plugins after copying the files.

## Installing a new Windrose server

The plugin installs/updates the official dedicated server through SteamCMD using:

```text
login anonymous
app_update 4129620
```

The main server executable used by the plugin is:

```text
R5\Binaries\Win64\WindroseServer-Win64-Shipping.exe
```

A typical WindowsGSM instance is located under:

```text
<WindowsGSM>\servers\<SERVER_ID>\serverfiles\
```

Important Windrose paths include:

```text
R5\ServerDescription.json
R5\Saved\SaveProfiles\Default\RocksDB_v2\
R5\Saved\SaveProfiles\Default\RocksDB\
R5WorldDescriptionUpdater.exe
```

## First clean launch

A truly new server may initially have no `ServerDescription.json`, no world, and no WorldGuard state file. WorldGuard allows that clean bootstrap so Windrose can create the first server identity and world.

Some Windrose builds may create the physical first world before fully persisting `WorldIslandId`. If exactly one valid active world exists and there is no conflicting locked identity, WorldGuard can safely adopt that world and write its ID back into the server configuration.

If multiple worlds exist and the identity is ambiguous, WorldGuard blocks startup rather than guessing.

## WindowsGSM configuration fields

### Server Name

Synchronizes with:

```text
ServerDescription.json
└── ServerDescription_Persistent
    └── ServerName
```

### Max Players

Synchronizes with:

```text
MaxPlayerCount
```

### Server Start Map

This field is used as the **friendly Windrose world name** only.

Example:

```text
Server Start Map = DELTA
```

synchronizes:

```json
"WorldName": "DELTA"
```

inside the active `WorldDescription.json`.

Changing this field does **not** change:

```text
WorldIslandId
islandId
PersistentServerId
InviteCode
Worlds\<WORLD_ID>\ directory name
```

Therefore, renaming the map/world in WindowsGSM keeps the same physical save and world identity.

On current `RocksDB_v2` worlds, the plugin runs `R5WorldDescriptionUpdater.exe` after changing `WorldName` so the edit is applied to the database. A backup of `WorldDescription.json` is created before the change. If the updater fails, startup is blocked and the plugin attempts a rollback.

### Server Port

When Direct Connection is enabled, the WindowsGSM Server Port is synchronized to:

```text
DirectConnectionServerPort
```

The selected direct-connection port should be available for both TCP and UDP.

Changing the port does not change the world identity or save.

### Query Port

Windrose does not use a traditional A2S query protocol through this plugin. The WindowsGSM Query Port field does not select or identify the world.

### Server IP Address

Changing the WindowsGSM IP address may affect networking, binding, firewall rules, routing, or port forwarding, but it does not change `WorldIslandId`, `islandId`, or the save directory.

## Additional Parameters

WorldGuard consumes the following plugin-specific WindowsGSM Additional Parameters:

```text
-password "your password"
-wr-region AUTO|EU|SEA|CIS
-wr-direct true|false
-wr-bind 0.0.0.0
-wr-autorestore true|false
-wr-adopt-world <WORLD_ID>
```

Example:

```text
-password "MyPassword" -wr-region EU -wr-direct true
```

### Password

```text
-password "MyPassword"
```

updates `Password` and `IsPasswordProtected` in `ServerDescription.json`.

To remove the password:

```text
-password ""
```

### Region

Examples:

```text
-wr-region AUTO
-wr-region EU
-wr-region SEA
-wr-region CIS
```

### Direct Connection

Enable:

```text
-wr-direct true
```

Disable:

```text
-wr-direct false
```

Optional bind/proxy address:

```text
-wr-bind 0.0.0.0
```

### Automatic broken-save recovery

```text
-wr-autorestore true
```

controls `AutoLoadLatestBackupIfHasBroken` when that field exists in the Windrose configuration schema.

### Intentional world switch

To intentionally adopt another **existing** world:

```text
-wr-adopt-world <WORLD_ID>
```

WorldGuard verifies that the requested world exists before changing the lock. Remove this parameter after a successful switch.

## ServerDescription.json

Location:

```text
<WindowsGSM>\servers\<SERVER_ID>\serverfiles\R5\ServerDescription.json
```

Critical persistent fields include:

```text
PersistentServerId
InviteCode
WorldIslandId
```

WorldGuard patches normal administrator-facing fields without intentionally replacing those persistent identity values. Unknown/new JSON fields are preserved because the plugin modifies the existing object instead of rebuilding the file from a fixed schema.

Do not casually edit the identity fields manually.

## Save locations

Current active database:

```text
R5\Saved\SaveProfiles\Default\RocksDB_v2\
```

Legacy active database:

```text
R5\Saved\SaveProfiles\Default\RocksDB\
```

Typical world path:

```text
<DB_ROOT>\<GAME_VERSION>\Worlds\<WORLD_ID>\WorldDescription.json
```

WorldGuard automatically considers active worlds from `RocksDB_v2` first and then legacy `RocksDB`.

Backup directories such as `RocksDB_v2_Backups` are deliberately not treated as active-world candidates. Restore a backup into the proper live database structure before attempting to use it as the active world.

## WorldDescription.json identity

Current Windrose files use the key:

```json
"islandId": "<WORLD_ID>"
```

WorldGuard reads lowercase `islandId` first and retains a compatibility fallback for `IslandId`.

The following should agree:

```text
ServerDescription.json -> WorldIslandId
WorldDescription.json -> islandId
Worlds\<WORLD_ID>\ directory name
```

If a genuinely empty `islandId` is discovered in an otherwise unambiguous valid world, WorldGuard can repair it from the physical world directory name. On `RocksDB_v2`, the official updater is then executed to apply the edit.

## R5WorldDescriptionUpdater.exe

The plugin searches for the updater in known locations including:

```text
<serverfiles>\R5WorldDescriptionUpdater.exe
<serverfiles>\R5\R5WorldDescriptionUpdater.exe
<serverfiles>\R5\Binaries\Win64\R5WorldDescriptionUpdater.exe
```

For modern `RocksDB_v2` worlds, plugin-owned changes to `WorldDescription.json` are applied through this executable. If it is missing when required, run a SteamCMD Update/Validate and try again.

## Migrating an existing/legacy dedicated server

For the full step-by-step procedures see:

- [English legacy migration guide](WINDROSE_LEGACY_MIGRATION_GUIDE_EN.txt)
- [Português do Brasil](WINDROSE_LEGACY_MIGRATION_GUIDE_PT-BR.txt)

At minimum, preserve and migrate:

```text
R5\ServerDescription.json
R5\Saved\
```

Recommended procedure:

1. Stop the old Windrose server completely.
2. Stop the new WindowsGSM Windrose instance.
3. Verify no Windrose server process is running.
4. Back up the entire old server.
5. Install the dedicated server through WindowsGSM, but do not bootstrap a replacement world when preserving an existing server.
6. Copy the old `R5\Saved\` into the WindowsGSM server's `R5\Saved\`.
7. Copy the original `R5\ServerDescription.json` into the WindowsGSM server.
8. If the WindowsGSM instance previously belonged to a different server identity, remove only the old `WindowsGSM_Windrose_State.json` as part of the deliberate migration.
9. Start through WindowsGSM.
10. Verify the expected world and player progress.
11. Stop and start again to confirm persistence.

## WorldGuard runtime files

State file:

```text
<SERVER_ROOT>\WindowsGSM_Windrose_State.json
```

Log:

```text
<SERVER_ROOT>\WindowsGSM_Windrose.log
```

Small identity/configuration backups:

```text
<SERVER_ROOT>\WindowsGSM_Windrose_Backups\
```

These backups may include copies of `ServerDescription.json`, the WorldGuard state, and `WorldDescription.json`. They do not replace a full backup of `R5\Saved\`.

## Troubleshooting

### Configured WorldIslandId not found

Do not repeatedly start Windrose manually. Check that the world exists in an active `RocksDB_v2` or `RocksDB` directory and that the configuration points to the correct World ID.

### WorldIslandId is empty

If a clean first bootstrap has created exactly one valid world, WorldGuard can adopt it. If multiple worlds exist, use an explicit `-wr-adopt-world <WORLD_ID>` rather than guessing.

### islandId mismatch

The physical world directory and `WorldDescription.json -> islandId` should match. WorldGuard blocks startup on a conflicting non-empty identity.

### PersistentServerId changed

Treat this as a server identity mismatch. Compare the current `ServerDescription.json`, the WorldGuard state, and backups before modifying or deleting anything.

### R5WorldDescriptionUpdater.exe missing/fails

Run SteamCMD Update/Validate and verify the updater is present. WorldGuard blocks unsafe `RocksDB_v2` edits rather than continuing with a potentially half-applied change.

### World name change fails

Check `WindowsGSM_Windrose.log`. The plugin backs up `WorldDescription.json`, attempts to apply `WorldName`, and blocks the server if the required database update cannot be completed safely.

## Safety rules

- Stop the server before manually copying or editing save data.
- Keep full backups of `R5\Saved\` and `R5\ServerDescription.json`.
- Do not rename `Worlds\<WORLD_ID>` directories without a deliberate migration plan.
- Do not casually modify `PersistentServerId`, `WorldIslandId`, or `islandId`.
- Do not run multiple server processes against the same save database.
- Do not delete WorldGuard state just to bypass an identity error.

## Update workflow

Before a major Windrose update:

1. Stop the server.
2. Back up `R5\Saved\`, `R5\ServerDescription.json`, and `WindowsGSM_Windrose_State.json`.
3. Use WindowsGSM Update/Validate.
4. Start through WindowsGSM.
5. Verify that WorldGuard loads the same locked world.

Windrose is an actively developed game, so major server/save-format changes should be checked against current Windrose dedicated-server documentation before manual database changes.

## Author

**BARCELOSTV / Luiz Augusto Barcelos**

- YouTube: https://www.youtube.com/@BARCELOSTV_OFICIAL/videos
- Twitch: https://www.twitch.tv/barcelostv
- Steam: https://steamcommunity.com/id/BARCELOSTV/

## License

The plugin source and documentation are distributed under the MIT License included in the repository. Third-party names, trademarks, logos, game assets, and proprietary software remain the property of their respective owners.
