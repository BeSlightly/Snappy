# Snapshot file maps

Snappy now tracks file mappings per history entry so different snapshots that replace the same vanilla path keep their own hashes.

## Data model
- `SnapshotInfo.FileMaps`: list of map entries `{ Id, BaseId, Changes, Timestamp }`.
- `SnapshotInfo.CurrentFileMapId`: points to the latest map entry.
- `HistoryEntryBase.FileMapId`: points to the map that was active when the entry was created.
- `FileReplacements`: still stored for compatibility, always mirrored from the resolved current map.

Each `FileMapEntry` stores only changed gamePath→hash pairs. Resolution follows `BaseId` chains (depth-limited) and overlays changes. A base entry is created automatically the first time a snapshot is saved or imported so legacy snapshots get anchored.

## Write path
- When saving a snapshot, Snappy builds the incoming map, computes changes vs the resolved current map, and appends a new `FileMapEntry` if anything changed. Existing history entries without a map id are backfilled to the current map.
- New Glamourer/Customize+ history entries record the current `FileMapId`.
- Importers (PCP/MCDF/migration) create an initial `FileMapEntry` so imported history points at a stable map.

## Read/export
- Applying a snapshot resolves the map id from the selected history entry (or falls back to the latest) and uses that map for Penumbra temp mods.
- PCP/PMP export resolves the same way; `ModPackageBuilder` now accepts a resolved map so exports always include the right hashes for the chosen entry.
