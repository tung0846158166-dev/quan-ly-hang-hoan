/** Bọc SQLite của Durable Object: đếm số hàng đọc/ghi thật (SqlStorageCursor) để hiển thị hạn mức. */
export type Row = Record<string, SqlStorageValue>;

export class Db {
  rowsRead = 0;
  rowsWritten = 0;
  constructor(private readonly sql: SqlStorage) {}

  all<T extends Row = Row>(query: string, ...bind: SqlStorageValue[]): T[] {
    const cur = this.sql.exec<T>(query, ...bind);
    const rows = cur.toArray();
    this.rowsRead += cur.rowsRead;
    this.rowsWritten += cur.rowsWritten;
    return rows;
  }

  first<T extends Row = Row>(query: string, ...bind: SqlStorageValue[]): T | undefined {
    return this.all<T>(query, ...bind)[0];
  }

  run(query: string, ...bind: SqlStorageValue[]): number {
    const cur = this.sql.exec(query, ...bind);
    cur.toArray();
    this.rowsRead += cur.rowsRead;
    this.rowsWritten += cur.rowsWritten;
    return cur.rowsWritten;
  }

  /** Thực thi nhiều câu lệnh (khởi tạo lược đồ), không tính vào số liệu. */
  exec(script: string): void {
    this.sql.exec(script);
  }
}

export const SCHEMA = `
CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS devices (
  device_id TEXT PRIMARY KEY, name TEXT NOT NULL, kind TEXT NOT NULL, role TEXT NOT NULL,
  token_hash TEXT NOT NULL UNIQUE, created_at INTEGER NOT NULL, last_seen_at INTEGER NOT NULL, revoked INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS pair_codes (code TEXT PRIMARY KEY, expires_at INTEGER NOT NULL, created_by TEXT NOT NULL, used INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS parcels (
  id TEXT PRIMARY KEY, rev INTEGER NOT NULL, updated_at INTEGER NOT NULL, data TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT '', is_archived INTEGER NOT NULL DEFAULT 0, is_deleted INTEGER NOT NULL DEFAULT 0, src_hash TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS parcels_rev ON parcels(rev);
CREATE TABLE IF NOT EXISTS parcel_codes (code TEXT NOT NULL, kind TEXT NOT NULL, parcel_id TEXT NOT NULL, PRIMARY KEY (code, kind, parcel_id)) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS parcel_codes_parcel ON parcel_codes(parcel_id);
CREATE TABLE IF NOT EXISTS return_watch (parcel_id TEXT PRIMARY KEY, since INTEGER NOT NULL, off INTEGER NOT NULL DEFAULT 0, rev INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS return_watch_rev ON return_watch(rev);
CREATE TABLE IF NOT EXISTS ops_done (op_id TEXT PRIMARY KEY, result TEXT NOT NULL, at INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS audit_log (id INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER NOT NULL, device_id TEXT NOT NULL, action TEXT NOT NULL, parcel_id TEXT NOT NULL, before TEXT NOT NULL, after TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS scans (id INTEGER PRIMARY KEY AUTOINCREMENT, parcel_id TEXT NOT NULL, device_id TEXT NOT NULL, at INTEGER NOT NULL, outcome TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS scans_parcel ON scans(parcel_id, device_id, at);
CREATE TABLE IF NOT EXISTS photos (
  photo_id TEXT PRIMARY KEY, parcel_id TEXT NOT NULL, device_id TEXT NOT NULL, mime TEXT NOT NULL,
  size INTEGER NOT NULL, thumb_size INTEGER NOT NULL DEFAULT 0, taken_at INTEGER NOT NULL, deleted_at INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS photos_parcel ON photos(parcel_id);
CREATE INDEX IF NOT EXISTS photos_taken ON photos(taken_at);
CREATE TABLE IF NOT EXISTS import_state (source TEXT PRIMARY KEY, signature TEXT NOT NULL, last_run_at INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS import_log (id INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER NOT NULL, source TEXT NOT NULL, sent INTEGER NOT NULL, added INTEGER NOT NULL, updated INTEGER NOT NULL, skipped INTEGER NOT NULL, ambiguous INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS import_issues (id INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER NOT NULL, source TEXT NOT NULL, data TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS quota (day TEXT PRIMARY KEY, requests INTEGER NOT NULL DEFAULT 0, rows_written INTEGER NOT NULL DEFAULT 0, rows_read INTEGER NOT NULL DEFAULT 0);
`;
