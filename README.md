# WindowsGSM.Windrose.WorldGuard

WindowsGSM plugin for the **Windrose Dedicated Server**, with persistent world protection, legacy migration support, and safe WindowsGSM configuration synchronization.

**Current version:** `2.4.0`  
**Windrose Dedicated Server AppID:** `4129620`  
**Platform:** Windows

<p align="center">
  <img src="Windrose.cs/Windrose.png" width="128" alt="Windrose Dedicated Server" />
</p>

## Why WorldGuard?

Some Windrose server setups can end up creating additional worlds when the world identity stored in `ServerDescription.json` no longer matches the saved world. WorldGuard validates the persistent identity before startup and blocks unsafe starts instead of silently allowing a replacement world to be generated.

The protected identity includes:

- `PersistentServerId`
- `InviteCode`
- `WorldIslandId`
- `WorldDescription.json -> islandId`
- the physical `Worlds\<WORLD_ID>\` save directory

## Features

- SteamCMD install/update support (`AppID 4129620`)
- World identity validation before startup
- Protection against accidental world regeneration
- Automatic first-world adoption when the identity is unambiguous
- Recovery of a missing `WorldIslandId` from the locked WorldGuard state
- Current `RocksDB_v2` and legacy `RocksDB` support
- Legacy dedicated-server migration support
- Safe `Server Start Map` -> Windrose `WorldName` synchronization
- `R5WorldDescriptionUpdater.exe` integration for `RocksDB_v2` world-description changes
- Direct Connection support through WindowsGSM Server Port
- Password and region synchronization
- Automatic identity/configuration backups
- Detailed plugin log and troubleshooting messages
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

Then restart WindowsGSM or reload plugins.

See [INSTALL.md](INSTALL.md) for the installation checklist.

## WindowsGSM fields

| WindowsGSM field | Windrose behavior |
| --- | --- |
| Server Name | Synchronizes `ServerDescription_Persistent.ServerName` |
| Max Players | Synchronizes `MaxPlayerCount` |
| Server Start Map | Synchronizes the friendly `WorldDescription.WorldName` only |
| Server Port | Used as `DirectConnectionServerPort` when Direct Connection is enabled |
| Query Port | Does not select or identify the Windrose world |
| Server IP Address | Network/admin setting; does not change world identity |

Changing **Server Start Map** never changes `WorldIslandId`, `islandId`, or the physical save directory.

## Additional Parameters

WorldGuard consumes the following WindowsGSM **Additional Parameters**:

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

`-wr-adopt-world` is intended only for an explicit switch to an already existing world. Remove it after the successful switch.

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

Do not start a fresh WindowsGSM Windrose server before copying the old server identity and save data if your intention is to preserve the existing server.

Detailed migration guides:

- [English legacy migration guide](docs/WINDROSE_LEGACY_MIGRATION_GUIDE_EN.txt)
- [Português do Brasil — migração de servidor legado](docs/WINDROSE_LEGACY_MIGRATION_GUIDE_PT-BR.txt)

## Full documentation

The complete configuration, WorldGuard behavior, backup paths, migration procedure, world switching and troubleshooting documentation is available in:

- [Complete WindowsGSM Windrose WorldGuard Guide](docs/WINDROSE_WINDOWS_GSM_GUIDE.md)
- [Changelog](CHANGELOG.md)

## Runtime files created by WorldGuard

Inside the WindowsGSM server's `serverfiles` directory the plugin can create:

```text
WindowsGSM_Windrose_State.json
WindowsGSM_Windrose.log
WindowsGSM_Windrose_Backups\
```

These files protect and document the server identity. They do not replace full backups of `R5\Saved\`.

## Safety notes

Before manually migrating, restoring or editing a Windrose save:

1. Stop the Windrose server completely.
2. Back up `R5\Saved\` and `R5\ServerDescription.json`.
3. Do not rename `Worlds\<WORLD_ID>` directories.
4. Do not casually modify `PersistentServerId`, `WorldIslandId` or `islandId`.
5. Do not run multiple server instances against the same save database.

If WorldGuard blocks a start, check `WindowsGSM_Windrose.log` before deleting or replacing any save files.

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
