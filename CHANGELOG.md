# Changelog

## 2.4.0

- WindowsGSM `Server Start Map` now synchronizes to Windrose `WorldDescription.WorldName`.
- Changing the map name keeps the same `WorldIslandId`, `islandId`, and physical save directory.
- Added automatic `R5WorldDescriptionUpdater.exe` application for `WorldName` changes on `RocksDB_v2`.
- Added `WorldDescription.json` backup before friendly-name synchronization.
- Added rollback attempt when the world-description updater fails.
- Blank WindowsGSM map names leave the existing Windrose `WorldName` unchanged.
- Existing World Guard identity protections remain unchanged.

## 2.3.0

- Fixed current Windrose `WorldDescription.json` identity key handling:
  - reads `islandId` (lowercase `i`)
  - retains compatibility fallback for `IslandId`
- Fixed false `IslandId is empty` errors on current Windrose worlds.
- Added guarded repair for a genuinely empty/missing `islandId`.
- Runs `R5WorldDescriptionUpdater.exe` after plugin-owned `islandId` repair on `RocksDB_v2`.
- Fixed locked-world recovery when `ServerDescription.json` has an empty `WorldIslandId`.
- Excluded `*_Backups` databases from automatic active-world selection.
- Deduplicated identical World IDs across version directories.
- Preserved independence of WindowsGSM Map/IP/Port/Query Port from Windrose world identity.
- Preserved existing save files; no automatic world folder rename/move is performed.

## 2.2.0

- Added automatic adoption of the only existing world after first bootstrap.
- Added missing WorldIslandId restoration from locked state.

## 2.1.0

- Decoupled WindowsGSM Server Start Map from Windrose WorldIslandId.
- Allowed incomplete clean bootstrap when no save exists.
