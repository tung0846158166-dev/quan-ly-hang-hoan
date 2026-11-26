import { fromWorkersAi, toWorkersAi, workersAiErrorStatus } from "./workersai";
import { buildNewParcel, ownCodes, parseNewOrder } from "./neworder";
import { answerText, buildStats, digestMessages, ruleText } from "./digest";
import { DEFAULT_AI_CONFIG, parseAiConfig, type AiConfig } from "./aiconfig";
import { DurableObject } from "cloudflare:workers";
import { decide, ReceiveStatus } from "../../core/src/scan";
import { Db, SCHEMA, type Row } from "./db";
import { canonical, codesOf, parseParcel, sha256Hex, type Parcel } from "./parcel";
import { returnWatchEligible } from "./returnwatch";
import { applyImport, type MOrder, type OtherEntry } from "../../core/src/merge";
import { lookupKeys, orderToParcel, parcelToOrder, parseIncoming, vnToday } from "./sheetimport";
import type { Env } from "./index";

const DAY_MS = 86_400_000;
const LIMITS = { requests: 100_000, rowsWritten: 100_000, rowsRead: 5_000_000 };
/** Đổi khi SCHEMA/nâng cấp lược đồ đổi: lần khởi động sau sẽ chạy lại migrate(). */
const SCHEMA_VERSION = "2026-11-26.4";

/** Lỗi Cloudflare khi hết lượt ghi Durable Object (gói miễn phí). */
export function isWriteQuotaError(e: unknown): boolean {
  return /Exceeded allowed rows written/i.test(String((e as { message?: unknown })?.message ?? e));
}
const VALID_STATUS = new Set<string>(Object.values(ReceiveStatus));
const EDITABLE_CELLS = new Set(["platformStatus", "carrier", "shipDate", "returnDate", "recvDate", "returnedAt", "deadline", "orderType", "price", "note", "userNote", "watchlistReason"]);
const PHOTO_ID = /^[A-Za-z0-9_-]{6,80}$/;
const MAX_PHOTO_BYTES = 1_000_000;
// ảnh gửi sau lượt quét: điện thoại giữ ảnh trong hàng chờ khi mất mạng rồi gửi lại (chủ dự án 26/11/2026) -> cho 3 ngày;
// vẫn chỉ nhận ảnh của kiện mà CHÍNH thiết bị đó đã quét xác nhận đúng một kiện.
const SCAN_PHOTO_WINDOW_MS = 3 * 86_400_000;

class ApiError extends Error {
  constructor(readonly status: number, readonly code: string, message: string) { super(message); }
}
const fail = (status: number, code: string, message: string): never => { throw new ApiError(status, code, message); };

interface Device { deviceId: string; name: string; kind: string; role: string }

/** Giờ Việt Nam (UTC+7) theo định dạng app cũ: dd/MM/yyyy HH:mm:ss */
export function vnTime(ms: number): string {
  const d = new Date(ms + 7 * 3_600_000);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${p(d.getUTCDate())}/${p(d.getUTCMonth() + 1)}/${d.getUTCFullYear()} ${p(d.getUTCHours())}:${p(d.getUTCMinutes())}:${p(d.getUTCSeconds())}`;
}

const json = (data: unknown, status = 200): Response => Response.json(data, { status });
const errorResponse = (e: ApiError): Response => json({ error: { code: e.code, message: e.message } }, e.status);

function b64url(bytes: Uint8Array): string {
  let s = "";
  for (const b of bytes) s += String.fromCharCode(b);
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function timingSafeEqual(a: string, b: string): boolean {
  const ea = new TextEncoder().encode(a), eb = new TextEncoder().encode(b);
  if (ea.length !== eb.length) return false;
  let r = 0;
  for (let i = 0; i < ea.length; i++) r |= (ea[i] as number) ^ (eb[i] as number);
  return r === 0;
}

export class ShopDO extends DurableObject<Env> {
  private db: Db;
  private pendingReq = 0;
  /** Ngày (UTC) Cloudflare đã chặn ghi vì hết lượt gói miễn phí: cả ngày đó coi như "stop". */
  private hardStopDay = "";
  private lastFlush = Date.now();
  private flushedWritten = 0;
  private flushedRead = 0;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.db = new Db(ctx.storage.sql);
    ctx.blockConcurrencyWhile(async () => {
      // Chỉ chạy lệnh tạo bảng khi phiên bản lược đồ đổi: CREATE ... IF NOT EXISTS vẫn bị Cloudflare tính là GHI, nên khi hết lượt ghi
      // trong ngày mà chạy mỗi lần khởi động thì MỌI yêu cầu (kể cả đọc) đều lỗi. Lỗi ở đây không được làm sập cả Durable Object.
      try {
        if (this.schemaVersion() === SCHEMA_VERSION) return;
        this.migrate();
      } catch (e) {
        if (isWriteQuotaError(e)) this.hardStopDay = this.dayKey();
        else console.error("khởi tạo lược đồ lỗi", e);
      }
    });
    // Ping/pong tự động, không đánh thức Durable Object (tiết kiệm yêu cầu).
    ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair('{"t":"ping"}', '{"t":"pong"}'));
  }

  private schemaVersion(): string {
    try { return String(this.db.first<Row>("SELECT v FROM meta WHERE k = 'schema'")?.v ?? ""); } catch { return ""; }
  }

  private migrate(): void {
      this.db.exec(SCHEMA);
      this.db.run("INSERT OR IGNORE INTO meta(k, v) VALUES ('rev', '0')");
      // cột "auto": 1 = dấu cloud TỰ thêm lúc nhập (dữ liệu cũ được phép thay giờ gốc vào), 0 = dấu gốc/đã có người đụng
      if (!this.db.all<Row>("PRAGMA table_info(return_watch)").some((c) => c.name === "auto"))
      {
        this.db.exec("ALTER TABLE return_watch ADD COLUMN auto INTEGER NOT NULL DEFAULT 0");
        // một lần lúc nâng cấp: dấu chưa ai bỏ/khôi phục coi như tự thêm (gửi lại dữ liệu cũ cùng giá trị thì không đổi gì)
        this.db.exec("UPDATE return_watch SET auto = 1 WHERE parcel_id NOT IN (SELECT parcel_id FROM audit_log WHERE action IN ('rwRemove','rwRestore'))");
      }
      // cột "keep": 1 = ảnh dữ liệu cũ chuyển từ máy tính, giữ vĩnh viễn (chủ dự án 26/11/2026); ảnh điện thoại chụp vẫn 90 ngày
      if (!this.db.all<Row>("PRAGMA table_info(photos)").some((c) => c.name === "keep")) {
        this.db.exec("ALTER TABLE photos ADD COLUMN keep INTEGER NOT NULL DEFAULT 0");
        // một lần lúc nâng cấp: ảnh do MÁY TÍNH tải lên (chỉ có thể là ảnh cũ chuyển sang) -> giữ
        this.db.exec("UPDATE photos SET keep = 1 WHERE device_id IN (SELECT device_id FROM devices WHERE kind = 'pc')");
      }
      this.db.run("INSERT OR REPLACE INTO meta(k, v) VALUES ('schema', ?)", SCHEMA_VERSION);
  }

  // ===================================================== điểm vào
  async fetch(req: Request): Promise<Response> {
    const url = new URL(req.url);
    const path = url.pathname.replace(/^\/v1/, "") || "/";
    try {
      await this.prime(req, url);
      const res = await this.route(req, url, path);
      this.countRequest();
      return res;
    } catch (e) {
      this.countRequest();
      if (e instanceof ApiError) return errorResponse(e);
      if (isWriteQuotaError(e)) {
        // Cloudflare hết lượt ghi gói miễn phí trong ngày: chặn MỌI truy vấn (kể cả SELECT) tới 00:00 UTC. Báo 429 để máy khách dừng và thử lại sau.
        this.hardStopDay = this.dayKey();
        console.warn("chặn ghi", path, (e as Error)?.stack);
        return errorResponse(new ApiError(429, "quota_exceeded", "Cloud tạm khóa tới 07:00 sáng (giờ Việt Nam): đã hết lượt ghi trong ngày của gói miễn phí. Dữ liệu không mất."));
      }
      console.error("lỗi không mong đợi", e);
      return json({ error: { code: "internal", message: "Lỗi máy chủ, thử lại sau." } }, 500);
    }
  }

  private async route(req: Request, url: URL, path: string): Promise<Response> {
    const m = req.method;
    // ---- không cần token
    if (m === "POST" && path === "/pair/bootstrap") return this.pairBootstrap(await this.body(req));
    if (m === "POST" && path === "/pair/complete") return this.pairComplete(await this.body(req));
    if (m === "POST" && path === "/pair/join") return this.pairJoin(await this.body(req));
    if (path === "/import" || path === "/import/state" || path === "/import/sheet" || path === "/import/issues") {
      const dev = this.authImport(req);
      if (m === "POST" && path === "/import") return this.importParcels(await this.body(req), dev);
      if (m === "GET" && path === "/import/state") return this.importState(url.searchParams.get("source") ?? "");
      if (m === "POST" && path === "/import/sheet") return this.importSheet(await this.body(req));
      if (m === "GET" && path === "/import/issues") return this.importIssues();
    }
    if (m === "GET" && path === "/ws") return this.upgradeWs(req, url);

    // ---- cần token thiết bị
    const dev = this.authDevice(req);
    if (m === "GET" && path === "/me") return json({ deviceId: dev.deviceId, deviceName: dev.name, role: dev.role });
    if (m === "POST" && path === "/pair/start") return this.pairStart(dev);
    if (m === "GET" && path === "/devices") return this.listDevices(dev);
    if (m === "DELETE" && path.startsWith("/devices/")) return this.revokeDevice(dev, decodeURIComponent(path.slice("/devices/".length)));
    if (m === "GET" && path === "/sync") return this.sync(url);
    if (m === "POST" && path === "/ops") return this.ops(await this.body(req), dev);
    if (m === "GET" && path === "/quota") return json(this.quotaSnapshot(true));
    if (m === "GET" && path === "/import/log") return this.importLog();
    if (m === "GET" && path === "/ai/digests") return this.listDigests(url);
    if (m === "POST" && path === "/admin/ai-digest") { this.requireOwner(dev); return json(await this.runDigest()); }
    if (m === "POST" && path === "/admin/purge-fresh-sheet") return this.purgeFreshSheet(await this.body(req), dev);
    if (m === "GET" && path === "/ai/status") return this.aiStatus();
    if (m === "PUT" && path === "/admin/ai-config") { this.requireOwner(dev); return this.putAiConfig(await this.body(req)); }
    const ai = /^\/ai\/(groq|gemini|nvidia|cloudflare)\/chat\/completions$/.exec(path);
    if (m === "POST" && ai) return this.aiProxy(req, ai[1] as AiProvider);
    if (m === "PUT" && path.startsWith("/photos/")) return this.putPhoto(req, url, decodeURIComponent(path.slice("/photos/".length)), dev);
    if (m === "GET" && path.startsWith("/photos/")) return this.getPhoto(url, decodeURIComponent(path.slice("/photos/".length)));
    const pp = /^\/parcels\/([^/]+)\/photos$/.exec(path);
    if (m === "GET" && pp) return this.parcelPhotos(decodeURIComponent(pp[1] as string));
    const ph = /^\/parcels\/([^/]+)\/history$/.exec(path);
    if (m === "GET" && ph) return this.parcelHistory(decodeURIComponent(ph[1] as string));
    return fail(404, "not_found", "Không có đường dẫn này.");
  }

  private async body(req: Request): Promise<Record<string, unknown>> {
    let v: unknown;
    try { v = await req.json(); } catch { return fail(422, "invalid", "Dữ liệu gửi lên không phải JSON hợp lệ."); }
    if (typeof v !== "object" || v === null || Array.isArray(v)) return fail(422, "invalid", "Dữ liệu gửi lên không hợp lệ.");
    return v as Record<string, unknown>;
  }

  // ===================================================== xác thực
  private tokenHash(token: string): Promise<string> { return sha256Hex(token); }

  private authDevice(req: Request): Device {
    const h = req.headers.get("Authorization") ?? "";
    const token = h.startsWith("Bearer ") ? h.slice(7).trim() : "";
    return this.deviceByToken(token);
  }

  /** Token đã được băm sẵn ở `prime` (băm là bất đồng bộ) nên tra cứu ở đây là đồng bộ. */
  private deviceByToken(token: string): Device {
    if (!token) return fail(401, "unauthorized", "Thiếu token thiết bị.");
    return this.lookupToken(token);
  }

  private tokenCache = new Map<string, string>();
  private lookupToken(token: string): Device {
    const hash = this.tokenCache.get(token);
    if (!hash) return fail(401, "unauthorized", "Token không hợp lệ.");
    const r = this.db.first<Row>("SELECT device_id, name, kind, role, revoked, last_seen_at FROM devices WHERE token_hash = ?", hash);
    if (!r || r.revoked) { this.tokenCache.delete(token); return fail(401, "unauthorized", "Thiết bị chưa được ghép hoặc đã bị thu hồi."); }
    const now = Date.now();
    // "lần cuối thấy máy" chỉ là thông tin phụ: không ghi khi đang bị chặn ghi, và lỗi ghi không được làm hỏng yêu cầu (đọc vẫn phải chạy)
    if (now - Number(r.last_seen_at) > 5 * 60_000 && this.hardStopDay !== this.dayKey()) {
      try { this.db.run("UPDATE devices SET last_seen_at = ? WHERE device_id = ?", now, r.device_id as string); }
      catch (e) { if (isWriteQuotaError(e)) this.hardStopDay = this.dayKey(); else throw e; }
    }
    return { deviceId: r.device_id as string, name: r.name as string, kind: r.kind as string, role: r.role as string };
  }

  private async prime(req: Request, url: URL): Promise<void> {
    const h = req.headers.get("Authorization") ?? "";
    const token = (h.startsWith("Bearer ") ? h.slice(7).trim() : "") || url.searchParams.get("token") || "";
    if (token && !this.tokenCache.has(token)) this.tokenCache.set(token, await this.tokenHash(token));
  }

  private authImport(req: Request): Device | null {
    const key = req.headers.get("X-Import-Key");
    if (key) {
      if (!this.env.IMPORT_KEY || !timingSafeEqual(key, this.env.IMPORT_KEY)) return fail(401, "unauthorized", "Khóa nhập không đúng.");
      return null;
    }
    return this.authDevice(req);
  }

  private requireOwner(dev: Device): void {
    if (dev.role !== "owner") fail(403, "forbidden", "Chỉ chủ cửa hàng mới làm được việc này.");
  }

  // ===================================================== ghép thiết bị
  private newToken(): string { return b64url(crypto.getRandomValues(new Uint8Array(32))); }

  private async createDevice(name: string, kind: string, role: string): Promise<{ deviceId: string; token: string; role: string }> {
    const token = this.newToken();
    const deviceId = crypto.randomUUID();
    const hash = await this.tokenHash(token);
    this.db.run("INSERT INTO devices(device_id, name, kind, role, token_hash, created_at, last_seen_at) VALUES (?,?,?,?,?,?,?)",
      deviceId, name, kind, role, hash, Date.now(), Date.now());
    return { deviceId, token, role };
  }

  private cleanName(v: unknown): string {
    const s = typeof v === "string" ? v.trim() : "";
    if (s.length === 0 || s.length > 80) fail(422, "invalid", "Tên thiết bị phải có từ 1 đến 80 ký tự.");
    return s;
  }
  private cleanKind(v: unknown): string {
    if (v !== "pc" && v !== "phone") fail(422, "invalid", "Loại thiết bị phải là pc hoặc phone.");
    return v as string;
  }

  private async pairBootstrap(b: Record<string, unknown>): Promise<Response> {
    const count = Number(this.db.first<Row>("SELECT COUNT(*) AS n FROM devices")?.n ?? 0);
    if (count > 0) return fail(403, "forbidden", "Đã có thiết bị; không dùng được khóa khởi tạo nữa.");
    const key = typeof b.bootstrapKey === "string" ? b.bootstrapKey : "";
    if (!this.env.BOOTSTRAP_KEY || !timingSafeEqual(key, this.env.BOOTSTRAP_KEY)) return fail(401, "unauthorized", "Khóa khởi tạo không đúng.");
    const d = await this.createDevice(this.cleanName(b.deviceName), this.cleanKind(b.kind), "owner");
    return json(d);
  }

  private async pairComplete(b: Record<string, unknown>): Promise<Response> {
    const now = Date.now();
    // chống dò mã 6 số: tối đa 20 lần sai mỗi 10 phút
    const failN = Number(this.meta("pairfail_n") ?? 0), failAt = Number(this.meta("pairfail_at") ?? 0);
    if (now - failAt < 600_000 && failN >= 20) return fail(429, "quota_exceeded", "Nhập sai quá nhiều lần. Thử lại sau 10 phút.");
    const code = typeof b.code === "string" ? b.code.trim() : "";
    const row = this.db.first<Row>("SELECT code, expires_at, used FROM pair_codes WHERE code = ?", code);
    if (!row || row.used || Number(row.expires_at) < now) {
      if (now - failAt >= 600_000) { this.setMeta("pairfail_n", "1"); this.setMeta("pairfail_at", String(now)); }
      else this.setMeta("pairfail_n", String(failN + 1));
      return fail(401, "unauthorized", "Mã ghép không đúng hoặc đã hết hạn.");
    }
    const name = this.cleanName(b.deviceName), kind = this.cleanKind(b.kind);
    this.db.run("UPDATE pair_codes SET used = 1 WHERE code = ?", code);
    return json(await this.createDevice(name, kind, "staff"));
  }

  /**
   * Ghép bằng MẬT KHẨU CỬA HÀNG (chủ tự đặt: `wrangler secret put JOIN_KEY`): điện thoại tự ghép, không cần máy tính tạo mã.
   * Mỗi máy vẫn có token riêng (vai trò nhân viên, thu hồi được từng máy). Chống dò: 10 lần sai / 15 phút thì khóa tạm.
   */
  private async pairJoin(b: Record<string, unknown>): Promise<Response> {
    const want = this.env.JOIN_KEY ?? "";
    if (want.length < 8) return fail(503, "unavailable", "Chủ cửa hàng chưa đặt mật khẩu cửa hàng trên cloud.");
    const now = Date.now();
    const failN = Number(this.meta("joinfail_n") ?? 0), failAt = Number(this.meta("joinfail_at") ?? 0);
    if (now - failAt < 900_000 && failN >= 10) return fail(429, "quota_exceeded", "Nhập sai mật khẩu quá nhiều lần. Thử lại sau 15 phút.");
    const key = typeof b.joinKey === "string" ? b.joinKey.trim() : "";
    if (!timingSafeEqual(key, want)) {
      if (now - failAt >= 900_000) { this.setMeta("joinfail_n", "1"); this.setMeta("joinfail_at", String(now)); }
      else this.setMeta("joinfail_n", String(failN + 1));
      return fail(401, "unauthorized", "Mật khẩu cửa hàng không đúng.");
    }
    return json(await this.createDevice(this.cleanName(b.deviceName), this.cleanKind(b.kind), "staff"));
  }

  private pairStart(dev: Device): Response {
    this.requireOwner(dev);
    const n = crypto.getRandomValues(new Uint32Array(1))[0] as number % 1_000_000;
    const code = String(n).padStart(6, "0");
    const expiresAt = Date.now() + 10 * 60_000;
    this.db.run("DELETE FROM pair_codes WHERE expires_at < ?", Date.now());
    this.db.run("INSERT OR REPLACE INTO pair_codes(code, expires_at, created_by, used) VALUES (?,?,?,0)", code, expiresAt, dev.deviceId);
    return json({ code, expiresAt });
  }

  private listDevices(dev: Device): Response {
    this.requireOwner(dev);
    const rows = this.db.all<Row>("SELECT device_id, name, kind, role, last_seen_at, revoked FROM devices ORDER BY created_at");
    return json(rows.map((r) => ({ deviceId: r.device_id, deviceName: r.name, kind: r.kind, role: r.role, lastSeenAt: r.last_seen_at, revoked: !!r.revoked })));
  }

  private revokeDevice(dev: Device, id: string): Response {
    this.requireOwner(dev);
    if (id === dev.deviceId) return fail(422, "invalid", "Không thu hồi chính thiết bị của chủ.");
    const n = this.db.run("UPDATE devices SET revoked = 1 WHERE device_id = ?", id);
    if (n === 0) return fail(404, "not_found", "Không thấy thiết bị.");
    this.tokenCache.clear();
    for (const ws of this.ctx.getWebSockets(id)) { try { ws.close(4001, "revoked"); } catch { /* đã đóng */ } }
    return json({ ok: true });
  }

  // ===================================================== meta / rev
  private meta(k: string): string | undefined { return this.db.first<Row>("SELECT v FROM meta WHERE k = ?", k)?.v as string | undefined; }
  private setMeta(k: string, v: string): void { this.db.run("INSERT INTO meta(k, v) VALUES (?, ?) ON CONFLICT(k) DO UPDATE SET v = excluded.v", k, v); }
  private nextRev(): number {
    const r = this.db.first<Row>("UPDATE meta SET v = CAST(v AS INTEGER) + 1 WHERE k = 'rev' RETURNING v");
    return Number(r?.v ?? 0);
  }
  private currentRev(): number { return Number(this.meta("rev") ?? 0); }

  // ===================================================== kiện
  private loadParcel(id: string): Parcel | undefined {
    const r = this.db.first<Row>("SELECT data FROM parcels WHERE id = ?", id);
    return r ? (JSON.parse(r.data as string) as Parcel) : undefined;
  }

  /** Ghi một kiện (thêm hoặc cập nhật): tăng rev, cập nhật bảng tra mã khi mã đổi. Gọi trong giao dịch. */
  private writeParcel(p: Parcel, srcHash?: string): number {
    const rev = this.nextRev();
    const now = Date.now();
    const data = JSON.stringify(p);
    if (srcHash === undefined) {
      this.db.run("UPDATE parcels SET rev = ?, updated_at = ?, data = ?, status = ?, is_archived = ?, is_deleted = ? WHERE id = ?",
        rev, now, data, p.status, p.isArchived ? 1 : 0, p.isDeleted ? 1 : 0, p.id);
    } else {
      this.db.run(`INSERT INTO parcels(id, rev, updated_at, data, status, is_archived, is_deleted, src_hash) VALUES (?,?,?,?,?,?,?,?)
        ON CONFLICT(id) DO UPDATE SET rev = excluded.rev, updated_at = excluded.updated_at, data = excluded.data, status = excluded.status,
        is_archived = excluded.is_archived, is_deleted = excluded.is_deleted, src_hash = excluded.src_hash`,
        p.id, rev, now, data, p.status, p.isArchived ? 1 : 0, p.isDeleted ? 1 : 0, srcHash);
    }
    this.refreshCodes(p);
    this.touchReturnWatch(p);
    return rev;
  }

  /** Đơn đủ điều kiện mà chưa có dấu thì TỰ vào Bảng Hàng Hoàn Về (một lần; không bao giờ đổi dòng đã có, kể cả đã bị bỏ). */
  private touchReturnWatch(p: Parcel): void {
    if (!returnWatchEligible(p)) return;
    if (this.db.first<Row>("SELECT 1 AS x FROM return_watch WHERE parcel_id = ?", p.id)) return;
    this.db.run("INSERT INTO return_watch(parcel_id, since, off, rev, auto) VALUES (?,?,0,?,1)", p.id, Date.now(), this.nextRev());
  }

  private refreshCodes(p: Parcel): void {
    const want = codesOf(p).map((c) => `${c.kind}|${c.code}`).sort();
    const have = this.db.all<Row>("SELECT kind, code FROM parcel_codes WHERE parcel_id = ?", p.id).map((r) => `${r.kind}|${r.code}`).sort();
    if (want.length === have.length && want.every((v, i) => v === have[i])) return;
    this.db.run("DELETE FROM parcel_codes WHERE parcel_id = ?", p.id);
    for (const c of codesOf(p)) this.db.run("INSERT OR IGNORE INTO parcel_codes(code, kind, parcel_id) VALUES (?,?,?)", c.code, c.kind, p.id);
  }

  private audit(deviceId: string, action: string, parcelId: string, before: unknown, after: unknown): void {
    this.db.run("INSERT INTO audit_log(at, device_id, action, parcel_id, before, after) VALUES (?,?,?,?,?,?)",
      Date.now(), deviceId, action, parcelId, JSON.stringify(before ?? null), JSON.stringify(after ?? null));
  }

  private toWire(p: Parcel, rev: number, updatedAt: number): Record<string, unknown> { return { ...p, rev, updatedAt }; }

  // ===================================================== đồng bộ
  private sync(url: URL): Response {
    const since = Math.max(0, Number(url.searchParams.get("since") ?? 0) || 0);
    const limit = Math.min(1000, Math.max(1, Number(url.searchParams.get("limit") ?? 500) || 500));
    // hai nguồn thay đổi (kiện + bảng hoàn về) trộn theo rev; mỗi nguồn lấy tối đa limit+1 dòng nhỏ nhất nên n dòng nhỏ nhất chung luôn nằm trong đó
    const parcels = this.db.all<Row>("SELECT data, rev, updated_at FROM parcels WHERE rev > ? ORDER BY rev LIMIT ?", since, limit + 1)
      .map((r) => ({ rev: Number(r.rev), change: { type: "parcel", rev: Number(r.rev), parcel: this.toWire(JSON.parse(r.data as string) as Parcel, Number(r.rev), Number(r.updated_at)) } as Record<string, unknown> }));
    const watch = this.db.all<Row>("SELECT parcel_id, since, off, rev FROM return_watch WHERE rev > ? ORDER BY rev LIMIT ?", since, limit + 1)
      .map((r) => ({ rev: Number(r.rev), change: { type: "returnWatch", rev: Number(r.rev), key: r.parcel_id, since: Number(r.since), off: !!r.off } as Record<string, unknown> }));
    const merged = [...parcels, ...watch].sort((a, b) => a.rev - b.rev);
    const more = merged.length > limit;
    const page = more ? merged.slice(0, limit) : merged;
    const rev = more ? (page[page.length - 1] as { rev: number }).rev : this.currentRev();
    return json({ rev, more, changes: page.map((x) => x.change) });
  }

  // ===================================================== thao tác ghi
  private async ops(b: Record<string, unknown>, dev: Device): Promise<Response> {
    const ops = Array.isArray(b.ops) ? (b.ops as Record<string, unknown>[]) : fail(422, "invalid", "Thiếu danh sách thao tác.");
    if (ops.length === 0 || ops.length > 50) fail(422, "invalid", "Mỗi lần gửi từ 1 đến 50 thao tác.");
    const results: unknown[] = [];
    let changed = false;
    for (const op of ops) {
      const opId = typeof op.opId === "string" ? op.opId : "";
      if (opId.length < 8 || opId.length > 80) { results.push({ opId, ok: false, error: { code: "invalid", message: "Thiếu hoặc sai opId." } }); continue; }
      const done = this.db.first<Row>("SELECT result FROM ops_done WHERE op_id = ?", opId);
      if (done) { results.push(JSON.parse(done.result as string)); continue; }
      let res: Record<string, unknown>;
      try {
        const quota = this.quotaSnapshot(false);
        if (op.type !== "scan" && quota.level === "stop")
          fail(429, "quota_exceeded", "Hết hạn mức hôm nay: thao tác ghi tạm dừng tới 07:00 sáng mai (giờ Việt Nam).");
        if (op.type === "scan" && quota.scanStop)
          fail(429, "quota_exceeded", "Cloud sắp hết lượt ghi hôm nay: tạm dừng quét tới 07:00 sáng (giờ Việt Nam) để tránh khóa cả cloud.");
        const before = this.currentRev();
        const result = this.ctx.storage.transactionSync(() => this.runOp(op, dev));
        const rev = this.currentRev();
        if (rev !== before) changed = true;
        res = { opId, ok: true, rev, result };
      } catch (e) {
        if (!(e instanceof ApiError)) throw e;
        res = { opId, ok: false, error: { code: e.code, message: e.message } };
      }
      this.db.run("INSERT OR REPLACE INTO ops_done(op_id, result, at) VALUES (?,?,?)", opId, JSON.stringify(res), Date.now());
      results.push(res);
    }
    if (changed) this.broadcast();
    return json({ results });
  }

  private runOp(op: Record<string, unknown>, dev: Device): Record<string, unknown> {
    switch (op.type) {
      case "scan": return this.opScan(op, dev);
      case "setStatus": return this.opSetStatus(op, dev);
      case "editNote": return this.opEditNote(op, dev);
      case "setWatch": return this.opMutate(op, dev, "setWatch", (p) => {
        p.isWatchlist = op.on === true;
        p.watchlistReason = p.isWatchlist ? this.text(op.reason, 500) : "";
      });
      case "archive": return this.opMutate(op, dev, "archive", (p) => { p.isArchived = true; p.archivedAt = vnTime(Date.now()); });
      case "unarchive": return this.opMutate(op, dev, "unarchive", (p) => { p.isArchived = false; p.archivedAt = ""; });
      case "delete": return this.opMutate(op, dev, "delete", (p) => { p.deletedFrom = p.isArchived ? "Kho lưu trữ" : "Bảng chính"; p.isDeleted = true; p.deletedAt = vnTime(Date.now()); });
      case "restore": return this.opMutate(op, dev, "restore", (p) => { p.isDeleted = false; p.deletedAt = ""; p.deletedFrom = ""; });
      case "editCell": return this.opEditCell(op, dev);
      case "newOrder": return this.opNewOrder(op, dev);
      case "rwRemove": return this.opReturnWatch(op, dev, true);
      case "rwRestore": return this.opReturnWatch(op, dev, false);
      default: return fail(422, "invalid", `Loại thao tác không hỗ trợ: ${String(op.type)}`);
    }
  }

  private text(v: unknown, max: number): string {
    const s = typeof v === "string" ? v : "";
    if (s.length > max) fail(422, "invalid", `Chữ quá dài (tối đa ${max} ký tự).`);
    return s;
  }

  private need(id: unknown): Parcel {
    const pid = typeof id === "string" ? id : "";
    const p = pid ? this.loadParcel(pid) : undefined;
    if (!p) return fail(404, "not_found", "Không tìm thấy kiện.");
    return p;
  }

  private opScan(op: Record<string, unknown>, dev: Device): Record<string, unknown> {
    const code = typeof op.code === "string" ? op.code : "";
    const key = normalizeForLookup(code);   // ứng viên lấy theo mã đã chuẩn hóa; quyết định cuối do decide() (cùng bộ ca thử với C#)
    const cands: Parcel[] = key.length === 0 ? [] : this.db.all<Row>(
      `SELECT DISTINCT p.data FROM parcel_codes c JOIN parcels p ON p.id = c.parcel_id
       WHERE c.code = ? AND p.is_archived = 0 AND p.is_deleted = 0`, key).map((r) => JSON.parse(r.data as string) as Parcel);
    const d = decide(cands, code);
    switch (d.kind) {
      case "notFound": return { outcome: "notFound", ack: false, message: "Không tìm thấy kiện." };
      case "ambiguous": return { outcome: "ambiguous", ack: false, message: `${d.candidates} kiện cùng khớp mã này, cần kiểm tra thủ công.` };
      case "needsManualCheck": return { outcome: "needsManualCheck", ack: false, message: `Trạng thái "${d.order?.status}": cần kiểm tra thủ công.`, parcel: d.order };
      case "alreadyReceived": {
        const o = d.order as Parcel;
        this.db.run("INSERT INTO scans(parcel_id, device_id, at, outcome) VALUES (?,?,?,?)", o.id, dev.deviceId, Date.now(), "alreadyReceived");
        const who = this.db.first<Row>(
          `SELECT d.name AS name, a.at AS at FROM audit_log a JOIN devices d ON d.device_id = a.device_id
           WHERE a.parcel_id = ? AND a.action = 'scan.receive' ORDER BY a.id DESC LIMIT 1`, o.id);
        return {
          outcome: "alreadyReceived", ack: true, message: "Kiện đã nhận trước đó.", parcel: o,
          receivedBy: who?.name ?? null, receivedAt: o.recvDate || (who ? vnTime(Number(who.at)) : null),
        };
      }
      case "receive": {
        const o = d.order as Parcel;
        const prev = o.status;
        o.status = ReceiveStatus.received;
        o.recvDate = vnTime(Date.now());
        const rev = this.writeParcel(o);   // một Durable Object xử lý tuần tự + giao dịch: không thể có hai máy cùng nhận một kiện
        this.audit(dev.deviceId, "scan.receive", o.id, { status: prev }, { status: o.status, recvDate: o.recvDate });
        this.db.run("INSERT INTO scans(parcel_id, device_id, at, outcome) VALUES (?,?,?,?)", o.id, dev.deviceId, Date.now(), "received");
        return { outcome: "received", ack: true, message: "Đã nhận kiện.", parcel: this.toWire(o, rev, Date.now()), receivedBy: dev.name, receivedAt: o.recvDate };
      }
    }
  }

  private opSetStatus(op: Record<string, unknown>, dev: Device): Record<string, unknown> {
    const status = typeof op.status === "string" ? op.status : "";
    if (!VALID_STATUS.has(status)) return fail(422, "invalid", "Trạng thái không hợp lệ.");
    return this.opMutate(op, dev, "setStatus", (p) => {
      if (typeof op.expectedStatus === "string" && p.status !== op.expectedStatus)
        fail(409, "conflict", `Trạng thái hiện tại là "${p.status}", không phải "${op.expectedStatus}". Tải lại rồi thử lại.`);
      p.status = status;
      if (status === ReceiveStatus.received && p.recvDate === "") p.recvDate = vnTime(Date.now());
    });
  }

  private opEditNote(op: Record<string, unknown>, dev: Device): Record<string, unknown> {
    return this.opMutate(op, dev, "editNote", (p) => {
      if (op.note !== undefined) p.note = this.text(op.note, 2000);
      if (op.userNote !== undefined) p.userNote = this.text(op.userNote, 2000);
    });
  }

  private opEditCell(op: Record<string, unknown>, dev: Device): Record<string, unknown> {
    const field = typeof op.field === "string" ? op.field : "";
    if (!EDITABLE_CELLS.has(field)) return fail(422, "invalid", `Ô không được sửa: ${field}`);
    return this.opMutate(op, dev, "editCell", (p) => {
      const rec = p as unknown as Record<string, unknown>;
      rec[field] = field === "price" ? Number(op.value) || 0 : this.text(op.value, 2000);
    });
  }

  /** Bỏ khỏi / khôi phục vào Bảng Hàng Hoàn Về (không xóa đơn). */
  private opReturnWatch(op: Record<string, unknown>, dev: Device, off: boolean): Record<string, unknown> {
    const p = this.need(op.id);
    const row = this.db.first<Row>("SELECT off FROM return_watch WHERE parcel_id = ?", p.id);
    if (!row) return fail(404, "not_found", "Đơn không nằm trong Bảng Hàng Hoàn Về.");
    if (!!row.off !== off) {
      this.db.run("UPDATE return_watch SET off = ?, auto = 0, rev = ? WHERE parcel_id = ?", off ? 1 : 0, this.nextRev(), p.id);
      this.audit(dev.deviceId, off ? "rwRemove" : "rwRestore", p.id, { off: !off }, { off });
    }
    return { off };
  }

  /** Đọc kiện → áp thay đổi → ghi + nhật ký (trước/sau). */
  private opMutate(op: Record<string, unknown>, dev: Device, action: string, apply: (p: Parcel) => void): Record<string, unknown> {
    const p = this.need(op.id);
    const before = JSON.parse(JSON.stringify(p)) as Parcel;
    apply(p);
    const rev = this.writeParcel(p);
    this.audit(dev.deviceId, action, p.id, diffOf(before, p, "before"), diffOf(before, p, "after"));
    return { parcel: this.toWire(p, rev, Date.now()) };
  }

  /**
   * Tạo đơn mới (máy tính nhập tay / điện thoại quét "Đơn mới"), cùng quy tắc NewOrderService của máy tính:
   * mã đã có ở bất kỳ đâu (kể cả lưu trữ, đã xóa) -> KHÔNG tạo lại; không hồi sinh đơn đã xóa.
   */
  private opNewOrder(op: Record<string, unknown>, dev: Device): Record<string, unknown> {
    const input = parseNewOrder(op);
    const code = normalizeForLookup(input.code);
    if (!code) return { outcome: "invalid", message: "Mã vận đơn trống", code: input.code };
    const oid = normalizeForLookup(input.orderId);
    const keys = [code, ...(oid ? [oid] : [])];
    const rows = this.db.all<Row>(
      `SELECT DISTINCT p.data FROM parcel_codes c JOIN parcels p ON p.id = c.parcel_id WHERE c.code IN (${keys.map(() => "?").join(",")})`, ...keys);
    const hit = rows.map((r) => JSON.parse(r.data as string) as Parcel)
      .find((p) => ownCodes(p).has(code) || (!!oid && normalizeForLookup(p.orderId) === oid && !p.track));
    if (hit) {
      const where = hit.isDeleted ? "mục Đã xóa" : hit.isArchived ? "Kho lưu trữ" : "Bảng chính";
      return { outcome: "duplicate", message: `ĐƠN ĐÃ CÓ Ở ${where.toUpperCase()}, KHÔNG TẠO LẠI`, code, orderId: hit.orderId, where };
    }
    const p = buildNewParcel("n_" + crypto.randomUUID().replace(/-/g, ""), input, vnTime(Date.now()));
    const rev = this.writeParcel(p, "");
    this.audit(dev.deviceId, input.origin === "phone" ? "phone_new_order" : "manual_new_order", p.id, {}, { track: p.track, platform: p.platform });
    return { outcome: "success", message: `ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH (${p.platform})`, code, platform: p.platform, orderId: p.orderId, parcel: this.toWire(p, rev, Date.now()) };
  }

  // ===================================================== nhập đơn
  private async importParcels(b: Record<string, unknown>, _dev: Device | null): Promise<Response> {
    const source = typeof b.source === "string" ? b.source : "";
    if (!["sheet", "file", "folder", "salework"].includes(source)) return fail(422, "invalid", "Nguồn nhập không hợp lệ.");
    const list = Array.isArray(b.parcels) ? b.parcels : fail(422, "invalid", "Thiếu danh sách kiện.");
    if (list.length > 500) fail(422, "invalid", "Tối đa 500 kiện mỗi lần.");
    if (this.quotaSnapshot(false).level === "stop") fail(429, "quota_exceeded", "Hết hạn mức hôm nay: nhập đơn tạm dừng tới 07:00 sáng mai (giờ Việt Nam).");

    // phần bất đồng bộ (băm) làm trước, phần ghi chạy trong một giao dịch
    const prepared: { p: Parcel; hash: string }[] = [];
    const rejected: { id: string; reason: string }[] = [];
    for (const raw of list) {
      const v = parseParcel(raw);
      if (!v.ok) { rejected.push({ id: typeof (raw as { id?: unknown })?.id === "string" ? (raw as { id: string }).id : "", reason: v.reason }); continue; }
      const given = typeof (raw as { hash?: unknown }).hash === "string" ? (raw as { hash: string }).hash : "";
      prepared.push({ p: v.parcel, hash: given || (await sha256Hex(canonical(v.parcel))) });
    }

    let added = 0, updated = 0, unchanged = 0, watchAdded = 0, watchFixed = 0;
    this.ctx.storage.transactionSync(() => {
      for (const { p, hash } of prepared) {
        const ex = this.db.first<Row>("SELECT data, src_hash FROM parcels WHERE id = ?", p.id);
        if (!ex) { this.writeParcel(p, hash); added++; continue; }
        if (ex.src_hash === hash) { unchanged++; continue; }
        const merged = mergeImport(JSON.parse(ex.data as string) as Parcel, p);
        this.writeParcel(merged, hash);
        updated++;
      }
      // dấu Bảng Hàng Hoàn Về của dữ liệu cũ: THÊM khi chưa có. Dòng đã có chỉ được thay khi đó là dấu cloud TỰ tạo lúc nhập
      // (chưa ai bỏ/khôi phục: không có nhật ký rwRemove/rwRestore) — để giữ đúng giờ hàng về gốc cho cảnh báo 12h/24h.
      for (const w of Array.isArray(b.returnWatch) ? (b.returnWatch as unknown[]).slice(0, 2000) : []) {
        const e = w as { id?: unknown; since?: unknown; off?: unknown };
        if (typeof e.id !== "string" || !this.db.first<Row>("SELECT 1 AS x FROM parcels WHERE id = ?", e.id)) continue;
        const since = Number(e.since) > 0 ? Number(e.since) : Date.now(), off = e.off === true ? 1 : 0;
        const ex = this.db.first<Row>("SELECT since, off, auto FROM return_watch WHERE parcel_id = ?", e.id);
        if (!ex) {
          this.db.run("INSERT INTO return_watch(parcel_id, since, off, rev) VALUES (?,?,?,?)", e.id, since, off, this.nextRev());
          watchAdded++;
          continue;
        }
        if (!Number(ex.auto)) continue;
        if (this.db.first<Row>("SELECT 1 AS x FROM audit_log WHERE parcel_id = ? AND action IN ('rwRemove','rwRestore')", e.id)) continue;
        this.db.run("UPDATE return_watch SET since = ?, off = ?, auto = 0, rev = ? WHERE parcel_id = ?", since, off, this.nextRev(), e.id);
        watchFixed++;
      }
      const sig = typeof b.signature === "string" ? b.signature.slice(0, 200) : "";
      this.db.run("INSERT INTO import_state(source, signature, last_run_at) VALUES (?,?,?) ON CONFLICT(source) DO UPDATE SET signature = excluded.signature, last_run_at = excluded.last_run_at",
        source, sig, Date.now());
      this.logImport(source, list.length, added, updated, rejected.length, 0);
    });
    if (added + updated + watchAdded + watchFixed > 0) this.broadcast();
    return json({ added, updated, unchanged, rejected, returnWatchAdded: watchAdded, returnWatchFixed: watchFixed });
  }

  /**
   * NHẬP TỪ GOOGLE SHEET (Apps Script đẩy lên): kiện đã chuyển dòng sẵn (ImportedParcel). Máy chủ đối chiếu với dữ liệu có sẵn bằng
   * CÙNG bộ gộp của app máy tính (cloud/core/src/merge.ts, đạt bộ ca thử C#): khớp đúng một kiện -> cập nhật; dòng cùng mã đơn chưa có mã
   * -> điền mã; đã lưu trữ -> chỉ bổ sung; đã xóa -> bỏ qua; nhập nhằng/khớp kiện của đơn khác -> KHÔNG đổi, ghi vào danh sách cần kiểm tra.
   */
  /**
   * Chỉ chủ cửa hàng. Xóa HẲN các kiện do Sheet tạo trên cloud (id "s_...") mà CHƯA từng có người đụng vào:
   * không quét, không ảnh, không sửa (nhật ký: gồm cả bỏ/khôi phục ở Bảng Hàng Hoàn Về), không lưu trữ/xóa. Dấu Bảng Hàng Hoàn Về TỰ thêm lúc nhập thì xóa theo.
   * Dùng khi Sheet lỡ nạp lên cloud TRƯỚC khi chuyển dữ liệu cũ (tránh tạo bản trùng). Kiện đã bị đụng vào thì giữ nguyên và báo lại.
   * Bắt buộc confirm = "XOA_KIEN_SHEET_CHUA_DUNG"; dryRun = true chỉ đếm.
   */
  private async purgeFreshSheet(b: Record<string, unknown>, dev: Device): Promise<Response> {
    this.requireOwner(dev);
    if (b.confirm !== "XOA_KIEN_SHEET_CHUA_DUNG") return fail(422, "invalid", "Thiếu xác nhận.");
    const dryRun = b.dryRun === true;
    const rows = this.db.all<Row>(
      `SELECT p.id AS id,
         (SELECT COUNT(*) FROM scans s WHERE s.parcel_id = p.id) AS sc,
         (SELECT COUNT(*) FROM photos f WHERE f.parcel_id = p.id) AS ph,
         (SELECT COUNT(*) FROM audit_log a WHERE a.parcel_id = p.id) AS au,
         p.is_archived AS ar, p.is_deleted AS de
       FROM parcels p WHERE p.id LIKE 's\\_%' ESCAPE '\\'`);
    const fresh = rows.filter((r) => !Number(r.sc) && !Number(r.ph) && !Number(r.au) && !Number(r.ar) && !Number(r.de)).map((r) => r.id as string);
    const kept = rows.length - fresh.length;
    if (!dryRun && fresh.length) {
      this.ctx.storage.transactionSync(() => {
        for (let i = 0; i < fresh.length; i += 90) {
          const chunk = fresh.slice(i, i + 90), q = chunk.map(() => "?").join(",");
          this.db.run(`DELETE FROM parcel_codes WHERE parcel_id IN (${q})`, ...chunk);
          this.db.run(`DELETE FROM return_watch WHERE parcel_id IN (${q})`, ...chunk);
          this.db.run(`DELETE FROM parcels WHERE id IN (${q})`, ...chunk);
        }
        this.db.run("DELETE FROM import_state WHERE source = 'sheet'");
      });
    }
    return json({ dryRun, purged: dryRun ? 0 : fresh.length, wouldPurge: fresh.length, kept });
  }

  private async importSheet(b: Record<string, unknown>): Promise<Response> {
    const list = Array.isArray(b.parcels) ? b.parcels : fail(422, "invalid", "Thiếu danh sách kiện.");
    if (list.length > 100) fail(422, "invalid", "Tối đa 100 kiện mỗi lần.");
    if (this.quotaSnapshot(false).level === "stop") fail(429, "quota_exceeded", "Hết hạn mức hôm nay: nhập Sheet tạm dừng tới 07:00 sáng mai (giờ Việt Nam).");
    const incoming = list.map(parseIncoming);
    const rejected = incoming.filter((x) => x === null).length;
    const parcels = incoming.filter((x): x is NonNullable<typeof x> => x !== null);

    // ứng viên: mọi kiện chung mã đơn / mã vận đơn / mã yêu cầu với lô này (theo thứ tự thêm vào kho)
    const keys = [...new Set(parcels.flatMap(lookupKeys))];
    const rows: Row[] = [];
    for (let i = 0; i < keys.length; i += 90) {
      const chunk = keys.slice(i, i + 90);
      rows.push(...this.db.all<Row>(
        `SELECT DISTINCT p.rowid AS rid, p.data FROM parcel_codes c JOIN parcels p ON p.id = c.parcel_id WHERE c.code IN (${chunk.map(() => "?").join(",")})`, ...chunk));
    }
    const seen = new Set<number>();
    const loaded = rows.filter((r) => !seen.has(Number(r.rid)) && seen.add(Number(r.rid))).sort((a, b) => Number(a.rid) - Number(b.rid))
      .map((r) => JSON.parse(r.data as string) as Parcel);
    const base = new Map(loaded.map((p) => [p.id, p]));
    const main: MOrder[] = loaded.filter((p) => !p.isArchived && !p.isDeleted).map(parcelToOrder);
    const others: OtherEntry[] = loaded.filter((p) => p.isArchived || p.isDeleted)
      .map((p) => ({ order: parcelToOrder(p), where: p.isDeleted ? "deleted" as const : "archive" as const }));

    const { today, year } = vnToday(Date.now());
    const res = applyImport(main, others, parcels, "supplement", today, year, () => "s_" + crypto.randomUUID().replace(/-/g, ""));

    const now = Date.now();
    let written = 0;
    this.ctx.storage.transactionSync(() => {
      const touched = new Map<string, MOrder>();
      for (const o of [...res.updated, ...res.enrichedArchived, ...res.added]) touched.set(o.id, o);
      for (const o of touched.values()) {
        const old = base.get(o.id) ?? null;
        const p = orderToParcel(o, old);
        if (!old) { p.importedAt = p.importedAt || vnTime(now); this.writeParcel(p, ""); }
        else if (JSON.stringify(old) !== JSON.stringify(p)) this.writeParcel(p, "");   // xóa dấu bản máy tính: bản gửi lại từ máy tính phải được áp dụng lại
        else continue;
        written++;
      }
      for (const a of res.ambiguous)
        this.db.run("INSERT INTO import_issues(at, source, data) VALUES (?,?,?)", now, "sheet", JSON.stringify(a));
      this.db.run("DELETE FROM import_issues WHERE id NOT IN (SELECT id FROM import_issues ORDER BY id DESC LIMIT 500)");
      this.logImport("sheet", list.length, res.added.length, res.updated.length, res.skippedDeleted, res.ambiguous.length);
      const sig = typeof b.signature === "string" ? b.signature.slice(0, 200) : "";
      this.db.run("INSERT INTO import_state(source, signature, last_run_at) VALUES ('sheet',?,?) ON CONFLICT(source) DO UPDATE SET signature = excluded.signature, last_run_at = excluded.last_run_at", sig, now);
    });
    if (written > 0) this.broadcast();
    return json({
      added: res.added.length, updated: res.updated.length, enrichedArchived: res.enrichedArchived.length, written,
      skippedDeleted: res.skippedDeleted, skippedArchivedUnchanged: res.skippedArchivedUnchanged, ambiguous: res.ambiguous, rejected,
    });
  }

  /** Một dòng lịch sử mỗi lô nhập (giữ 2.000 dòng gần nhất). */
  private logImport(source: string, sent: number, added: number, updated: number, skipped: number, ambiguous: number): void {
    this.db.run("INSERT INTO import_log(at, source, sent, added, updated, skipped, ambiguous) VALUES (?,?,?,?,?,?,?)",
      Date.now(), source, sent, added, updated, skipped, ambiguous);
    this.db.run("DELETE FROM import_log WHERE id NOT IN (SELECT id FROM import_log ORDER BY id DESC LIMIT 2000)");
  }

  /** Lịch sử đồng bộ: các lô cùng nguồn cách nhau < 3 phút gộp thành MỘT lượt (Apps Script gửi theo lô 50). Mới nhất trước. */
  private importLog(): Response {
    const rows = this.db.all<Row>("SELECT at, source, sent, added, updated, skipped, ambiguous FROM import_log ORDER BY id ASC");
    const runs: { source: string; start: number; end: number; batches: number; sent: number; added: number; updated: number; skipped: number; ambiguous: number }[] = [];
    const last = new Map<string, (typeof runs)[number]>();
    for (const r of rows) {
      const at = Number(r.at), src = String(r.source);
      let run = last.get(src);
      if (!run || at - run.end > 180_000) {
        run = { source: src, start: at, end: at, batches: 0, sent: 0, added: 0, updated: 0, skipped: 0, ambiguous: 0 };
        runs.push(run); last.set(src, run);
      }
      run.end = at; run.batches++;
      run.sent += Number(r.sent); run.added += Number(r.added); run.updated += Number(r.updated);
      run.skipped += Number(r.skipped); run.ambiguous += Number(r.ambiguous);
    }
    return json(runs.reverse().slice(0, 200));
  }

  private importIssues(): Response {
    const rows = this.db.all<Row>("SELECT at, source, data FROM import_issues ORDER BY id DESC LIMIT 200");
    return json(rows.map((r) => ({ at: Number(r.at), source: r.source, ...JSON.parse(r.data as string) })));
  }

  private importState(source: string): Response {
    const r = this.db.first<Row>("SELECT signature, last_run_at FROM import_state WHERE source = ?", source);
    const n = Number(this.db.first<Row>("SELECT COUNT(*) AS n FROM parcels")?.n ?? 0);
    return json({ signature: r?.signature ?? "", lastRunAt: r ? Number(r.last_run_at) : 0, parcelCount: n });
  }

  // ===================================================== ảnh
  private async putPhoto(req: Request, url: URL, photoId: string, dev: Device): Promise<Response> {
    if (!PHOTO_ID.test(photoId)) return fail(422, "invalid", "Mã ảnh không hợp lệ.");
    const parcelId = url.searchParams.get("parcelId") ?? "";
    const kind = url.searchParams.get("kind") === "thumb" ? "thumb" : "full";
    const mime = (url.searchParams.get("mime") ?? "image/jpeg").toLowerCase();
    if (mime !== "image/jpeg" && mime !== "image/png" && mime !== "image/webp") return fail(422, "invalid", "Chỉ nhận ảnh JPEG, PNG hoặc WebP.");
    // Chuyển ảnh CŨ lên (công cụ nhập dữ liệu, chỉ chủ cửa hàng): không cần vừa quét, giữ giờ chụp gốc nếu còn trong hạn lưu.
    const legacy = url.searchParams.get("legacy") === "1";
    if (legacy && dev.role !== "owner") return fail(403, "forbidden", "Chỉ chủ cửa hàng mới chuyển được ảnh cũ.");
    if (!legacy && dev.kind === "pc") return fail(403, "forbidden", "Chỉ điện thoại mới được tải ảnh lên.");
    let takenAt = Date.now();
    if (legacy && url.searchParams.has("takenAt")) {
      const t = Number(url.searchParams.get("takenAt"));
      if (!Number.isFinite(t) || t <= 0) return fail(422, "invalid", "Giờ chụp không hợp lệ.");
      takenAt = Math.min(t, Date.now());   // ảnh cũ: giữ giờ chụp gốc, không giới hạn tuổi (giữ vĩnh viễn)
    }
    const parcel = this.loadParcel(parcelId);
    if (!parcel) return fail(404, "not_found", "Không tìm thấy kiện.");
    const existing = this.db.first<Row>("SELECT device_id, parcel_id FROM photos WHERE photo_id = ?", photoId);
    if (existing && existing.parcel_id !== parcelId) return fail(409, "conflict", "Mã ảnh đã thuộc kiện khác.");
    if (!existing && !legacy) {
      const scan = this.db.first<Row>(
        "SELECT at FROM scans WHERE parcel_id = ? AND device_id = ? AND outcome IN ('received','alreadyReceived') ORDER BY at DESC LIMIT 1", parcelId, dev.deviceId);
      if (!scan || Date.now() - Number(scan.at) > SCAN_PHOTO_WINDOW_MS)
        return fail(409, "conflict", "Chỉ lưu ảnh cho kiện vừa được quét xác nhận đúng một kiện bởi chính thiết bị này.");
    }
    const bytes = await req.arrayBuffer();
    if (bytes.byteLength === 0) return fail(422, "invalid", "Ảnh rỗng.");
    if (bytes.byteLength > MAX_PHOTO_BYTES) return fail(413, "invalid", "Ảnh quá lớn (tối đa 1 MB sau khi thu nhỏ).");
    const sha = [...new Uint8Array(await crypto.subtle.digest("SHA-256", bytes))].map((x) => x.toString(16).padStart(2, "0")).join("");
    await this.env.IMAGES.put(`${kind === "thumb" ? "t" : "p"}/${photoId}`, bytes, { httpMetadata: { contentType: mime } });
    let attached = false;
    this.ctx.storage.transactionSync(() => {
      if (!existing) this.db.run("INSERT INTO photos(photo_id, parcel_id, device_id, mime, size, thumb_size, taken_at, keep) VALUES (?,?,?,?,?,?,?,?)",
        photoId, parcelId, dev.deviceId, mime, kind === "full" ? bytes.byteLength : 0, kind === "thumb" ? bytes.byteLength : 0, takenAt, legacy ? 1 : 0);
      else this.db.run(kind === "full" ? "UPDATE photos SET size = ?, mime = ? WHERE photo_id = ?" : "UPDATE photos SET thumb_size = ?, mime = ? WHERE photo_id = ?", bytes.byteLength, mime, photoId);
      // ảnh cũ: chỉ gắn khi kiện chưa có ảnh HOẶC đang trỏ mã ảnh của máy tính (không có trên cloud, vd. "ph-..."); không đè ảnh cloud đang gắn
      const attachLegacy = parcel.photoId === "" || !this.db.first<Row>("SELECT 1 AS x FROM photos WHERE photo_id = ? AND deleted_at = 0", parcel.photoId);
      if (kind === "full" && parcel.photoId !== photoId && (!legacy || attachLegacy)) {
        const before = parcel.photoId;
        parcel.photoId = photoId;
        this.writeParcel(parcel);
        this.audit(dev.deviceId, "photo.attach", parcelId, { photoId: before }, { photoId });
        attached = true;
      }
    });
    // chỉ báo các máy khi đơn thật sự đổi (ảnh gắn vào đơn); ảnh thêm không đổi đơn thì không báo
    // (chuyển hàng nghìn ảnh cũ từng làm máy tính/điện thoại đồng bộ liên tục, nhấp nháy - 26/11/2026)
    if (attached) this.broadcast();
    return json({ ok: true, photoId, sha256: sha });
  }

  private async getPhoto(url: URL, photoId: string): Promise<Response> {
    const row = this.db.first<Row>("SELECT mime, deleted_at FROM photos WHERE photo_id = ?", photoId);
    if (!row) return fail(404, "not_found", "Không thấy ảnh.");
    if (Number(row.deleted_at) > 0) return fail(410, "gone", "Ảnh đã hết hạn lưu (tự xóa sau 90 ngày).");
    const wantThumb = url.searchParams.get("size") === "thumb";
    let obj = wantThumb ? await this.env.IMAGES.get(`t/${photoId}`) : null;
    if (!obj) obj = await this.env.IMAGES.get(`p/${photoId}`);
    if (!obj) return fail(404, "not_found", "Không thấy tệp ảnh.");
    return new Response(obj.body, { headers: { "Content-Type": String(row.mime), "Cache-Control": "private, max-age=2592000" } });
  }

  private parcelPhotos(parcelId: string): Response {
    const rows = this.db.all<Row>(
      `SELECT p.photo_id, p.taken_at, p.deleted_at, d.name FROM photos p LEFT JOIN devices d ON d.device_id = p.device_id
       WHERE p.parcel_id = ? ORDER BY p.taken_at DESC`, parcelId);
    return json(rows.map((r) => ({ photoId: r.photo_id, takenAt: r.taken_at, deviceName: r.name ?? "", expired: Number(r.deleted_at) > 0 })));
  }

  /** Lịch sử sửa của một kiện (nhật ký trước/sau), mới nhất trước, tối đa 200 dòng. */
  private parcelHistory(parcelId: string): Response {
    const rows = this.db.all<Row>(
      `SELECT a.at, a.action, a.before, a.after, COALESCE(d.name, a.device_id) AS who FROM audit_log a
       LEFT JOIN devices d ON d.device_id = a.device_id WHERE a.parcel_id = ? ORDER BY a.id DESC LIMIT 200`, parcelId);
    return json(rows.map((r) => ({ at: r.at, action: r.action, deviceName: r.who, before: JSON.parse(r.before as string), after: JSON.parse(r.after as string) })));
  }

  // ===================================================== cổng AI (khóa Groq/Gemini chỉ nằm trên cloud)
  private aiLimit(): number { return Math.max(1, Number(this.env.AI_DAILY_LIMIT) || 3000); }

  private aiUsedToday(): number {
    return this.meta("ai_day") === this.dayKey() ? Number(this.meta("ai_n") ?? 0) : 0;
  }

  private aiStatus(): Response {
    const cfg = this.aiConfig();
    return json({
      groq: !!this.env.GROQ_API_KEY, gemini: !!this.env.GEMINI_API_KEY, nvidia: !!this.env.NVIDIA_API_KEY, cloudflare: !!this.workersAiBinding(),
      usedToday: this.aiUsedToday(), dailyLimit: this.aiLimit(),
      // cấu hình chung: app mới làm theo (thứ tự dự phòng, mô hình, thời gian chờ); app cũ bỏ qua các trường này
      order: cfg.order, models: cfg.models, timeoutSec: cfg.timeoutSec,
    });
  }

  /** Cấu hình AI chung (meta "ai_config"); chưa đặt hoặc hỏng thì dùng mặc định. */
  private aiConfig(): AiConfig {
    const raw = this.meta("ai_config");
    if (!raw) return DEFAULT_AI_CONFIG;
    try { const c = parseAiConfig(JSON.parse(raw)); return typeof c === "string" ? DEFAULT_AI_CONFIG : c; } catch { return DEFAULT_AI_CONFIG; }
  }

  private putAiConfig(b: Record<string, unknown>): Response {
    const c = parseAiConfig(b, this.aiConfig());
    if (typeof c === "string") return fail(422, "invalid", c);
    this.setMeta("ai_config", JSON.stringify(c));
    return json(c);
  }

  /**
   * Chuyển tiếp yêu cầu kiểu OpenAI tới Groq/Gemini bằng khóa của cloud. Máy khách dùng NGUYÊN logic gọi AI hiện có, chỉ đổi địa chỉ gốc
   * sang `/v1/ai/<nhà-cung-cấp>` và dùng token thiết bị thay cho khóa API. Không ghi lại nội dung hỏi/đáp (chỉ đếm số lần gọi trong ngày).
   */
  private async aiProxy(req: Request, provider: AiProvider): Promise<Response> {
    const key = provider === "groq" ? this.env.GROQ_API_KEY : provider === "gemini" ? this.env.GEMINI_API_KEY
      : provider === "nvidia" ? this.env.NVIDIA_API_KEY : (this.workersAiBinding() ? "binding" : undefined);
    if (!key) return fail(503, "ai_not_configured", "Chưa cấu hình khóa AI trên cloud (chủ cửa hàng đặt khóa bằng wrangler secret put).");
    if (this.aiUsedToday() >= this.aiLimit())
      return fail(429, "ai_quota", "Hôm nay đã dùng hết số lượt hỏi AI cho cả cửa hàng. Thử lại sau 07:00 sáng mai (giờ Việt Nam).");
    if (this.quotaSnapshot(false).level === "stop") return fail(429, "quota_exceeded", "Hết hạn mức cloud hôm nay, tạm dừng hỏi AI.");
    const body = await req.arrayBuffer();
    if (body.byteLength === 0 || body.byteLength > 1_000_000) return fail(422, "invalid", "Nội dung gửi AI rỗng hoặc quá lớn.");
    this.setMeta("ai_day", this.dayKey());
    this.setMeta("ai_n", String(this.aiUsedToday() + 1));
    if (provider === "cloudflare") return this.workersAi(body);
    const base = AI_BASE[provider];
    let up: Response;
    try {
      up = await fetch(`${base}/chat/completions`, {
        method: "POST", body, signal: AbortSignal.timeout(90_000),
        headers: { "Content-Type": "application/json", Authorization: `Bearer ${key}` },
      });
    } catch {
      return fail(503, "unavailable", "Không kết nối được tới nhà cung cấp AI. Thử lại sau.");
    }
    const headers = new Headers({ "Content-Type": up.headers.get("Content-Type") ?? "application/json" });
    const ra = up.headers.get("Retry-After");
    if (ra) headers.set("Retry-After", ra);
    return new Response(up.body, { status: up.status, headers });
  }

  /** Binding Workers AI (AI_OFF=1 khi chạy thử cục bộ: không gọi Workers AI thật). */
  private workersAiBinding(): Ai | undefined { return this.env.AI_OFF === "1" ? undefined : this.env.AI; }

  /** Workers AI gắn thẳng vào Worker (binding AI): đổi chuẩn OpenAI <-> Workers AI (src/workersai.ts). */
  private async workersAi(body: ArrayBuffer): Promise<Response> {
    let req: Record<string, unknown>;
    try { req = JSON.parse(new TextDecoder().decode(body)) as Record<string, unknown>; }
    catch { return fail(422, "invalid", "Nội dung gửi AI không phải JSON."); }
    const call = toWorkersAi(req);
    const ai = this.workersAiBinding()!;
    try {
      // mô hình hiểu thẳng chuẩn OpenAI (tin "tool", tool_calls): gửi nguyên văn để không mất ngữ cảnh gọi công cụ
      const native: Record<string, unknown> = { messages: req.messages };
      for (const k of ["tools", "tool_choice", "temperature", "max_tokens"]) if (req[k] !== undefined) native[k] = req[k];
      let out: unknown;
      try { out = await ai.run(call.model as Parameters<Ai["run"]>[0], native as never); }
      catch (e) {
        if (workersAiErrorStatus(e) === 429) throw e;
        out = await ai.run(call.model as Parameters<Ai["run"]>[0], call.input as never);   // mô hình cũ: dạng riêng của Workers AI
      }
      return json(fromWorkersAi(call.model, out));
    } catch (e) {
      const status = workersAiErrorStatus(e);
      return new Response(JSON.stringify({ error: { message: "Workers AI: " + String((e as Error)?.message ?? e).slice(0, 300) } }),
        { status, headers: { "Content-Type": "application/json" } });
    }
  }

  // ===================================================== thời gian thực
  private async upgradeWs(req: Request, url: URL): Promise<Response> {
    if (req.headers.get("Upgrade") !== "websocket") return fail(426, "invalid", "Cần nâng cấp WebSocket.");
    const dev = this.deviceByToken(url.searchParams.get("token") ?? "");
    const pair = new WebSocketPair();
    this.ctx.acceptWebSocket(pair[1], [dev.deviceId]);
    pair[1].send(JSON.stringify({ t: "rev", rev: this.currentRev() }));
    return new Response(null, { status: 101, webSocket: pair[0] });
  }

  webSocketMessage(_ws: WebSocket, _message: string | ArrayBuffer): void { /* ping/pong do auto-response xử lý */ }
  webSocketClose(ws: WebSocket, code: number): void { try { ws.close(code, "bye"); } catch { /* đã đóng */ } }

  private broadcast(): void {
    const msg = JSON.stringify({ t: "rev", rev: this.currentRev() });
    for (const ws of this.ctx.getWebSockets()) { try { ws.send(msg); } catch { /* socket hỏng */ } }
  }

  // ===================================================== hạn mức
  private dayKey(): string { return new Date().toISOString().slice(0, 10); }

  private countRequest(): void {
    this.pendingReq++;
    const now = Date.now();
    if (this.hardStopDay === this.dayKey()) return;   // đang bị chặn ghi: đừng ghi bảng hạn mức
    if (this.pendingReq >= 10 || now - this.lastFlush > 60_000) {
      try { this.flushQuota(); } catch (e) { if (isWriteQuotaError(e)) this.hardStopDay = this.dayKey(); else throw e; }
    }
  }

  private flushQuota(): void {
    const day = this.dayKey();
    const w = this.db.rowsWritten - this.flushedWritten, r = this.db.rowsRead - this.flushedRead;
    // ghi chính bảng hạn mức cũng tốn khoảng 2 hàng ghi
    this.db.run(`INSERT INTO quota(day, requests, rows_written, rows_read) VALUES (?,?,?,?)
      ON CONFLICT(day) DO UPDATE SET requests = requests + excluded.requests, rows_written = rows_written + excluded.rows_written, rows_read = rows_read + excluded.rows_read`,
      day, this.pendingReq, w + 2, r);
    this.flushedWritten = this.db.rowsWritten; this.flushedRead = this.db.rowsRead;
    this.pendingReq = 0; this.lastFlush = Date.now();
  }

  private quotaSnapshot(flush: boolean): Record<string, unknown> & { level: string; scanStop: boolean } {
    const day = this.dayKey();
    if (flush && this.hardStopDay !== day) {
      try { this.flushQuota(); } catch (e) { if (isWriteQuotaError(e)) this.hardStopDay = day; else throw e; }
    }
    const row = this.db.first<Row>("SELECT requests, rows_written, rows_read FROM quota WHERE day = ?", day);
    const requests = Number(row?.requests ?? 0) + this.pendingReq;
    const written = Number(row?.rows_written ?? 0) + (this.db.rowsWritten - this.flushedWritten);
    const read = Number(row?.rows_read ?? 0) + (this.db.rowsRead - this.flushedRead);
    const frac = Math.max(requests / LIMITS.requests, written / LIMITS.rowsWritten, read / LIMITS.rowsRead);
    // Bộ đếm của ta THẤP hơn số Cloudflare tính (đo 26/11/2026: ta đếm 88.900 khi Cloudflare đã chặn ở 100.000 — Cloudflare tính cả ghi chỉ mục).
    // Chạm trần thật thì Cloudflare chặn cả ĐỌC tới 07:00 VN => dừng nhập/sửa từ 75%, cảnh báo từ 50%; quét nhận hàng vẫn chạy tới đó.
    const level = this.hardStopDay === day || frac >= 0.75 ? "stop" : frac >= 0.5 ? "warn" : "ok";
    return {
      day, requests: { used: requests, limit: LIMITS.requests }, rowsWritten: { used: written, limit: LIMITS.rowsWritten },
      rowsRead: { used: read, limit: LIMITS.rowsRead }, level, scanStop: this.hardStopDay === day || frac >= 0.9,
    };
  }

  // ===================================================== dọn định kỳ (Cron)
  /**
   * Gợi ý AI theo khung giờ làm việc (cron: 8:00, 10:30, 13:30, 16:30, 19:00, 21:00 giờ Việt Nam). Số liệu tổng hợp, không dữ liệu khách.
   * Thứ tự: Workers AI trong Worker -> Groq -> bản viết theo quy tắc. Luôn lưu một bản (không bỏ lượt).
   */
  async runDigest(): Promise<{ id: number; at: number; slot: string; text: string; byAi: boolean }> {
    const now = Date.now();
    const parcels = this.db.all<Row>("SELECT data FROM parcels WHERE is_archived = 0 AND is_deleted = 0").map((r) => JSON.parse(r.data as string) as Parcel);
    const amb = Number(this.db.first<Row>("SELECT COUNT(*) AS n FROM import_issues WHERE at > ?", now - DAY_MS)?.n ?? 0);
    const stats = buildStats(parcels, now, amb);
    let text = "", byAi = false;
    const canAi = this.aiUsedToday() < this.aiLimit() && this.quotaSnapshot(false).level !== "stop";
    if (canAi) {
      const messages = digestMessages(stats);
      const ai = this.workersAiBinding();
      if (ai) {
        try { text = answerText(fromWorkersAi("@cf/openai/gpt-oss-120b", await ai.run("@cf/openai/gpt-oss-120b" as Parameters<Ai["run"]>[0], { messages } as never))); }
        catch { text = ""; }
      }
      if (!text && this.env.GROQ_API_KEY) {
        try {
          const r = await fetch("https://api.groq.com/openai/v1/chat/completions", {
            method: "POST", signal: AbortSignal.timeout(30_000),
            headers: { "Content-Type": "application/json", Authorization: `Bearer ${this.env.GROQ_API_KEY}` },
            body: JSON.stringify({ model: "openai/gpt-oss-120b", messages }),
          });
          if (r.ok) text = answerText(await r.json());
        } catch { text = ""; }
      }
      if (text) { byAi = true; this.setMeta("ai_day", this.dayKey()); this.setMeta("ai_n", String(this.aiUsedToday() + 1)); }
    }
    if (!text) text = ruleText(stats);
    this.db.run("INSERT INTO ai_digest(at, slot, text, by_ai, stats) VALUES (?,?,?,?,?)", now, stats.slot, text, byAi ? 1 : 0, JSON.stringify(stats));
    this.db.run("DELETE FROM ai_digest WHERE id NOT IN (SELECT id FROM ai_digest ORDER BY id DESC LIMIT 200)");
    const id = Number(this.db.first<Row>("SELECT MAX(id) AS id FROM ai_digest")?.id ?? 0);
    return { id, at: now, slot: stats.slot, text, byAi };
  }

  /** Các bản gợi ý gần nhất (mới nhất trước). Máy nào đã ghép cũng xem được. */
  private listDigests(url: URL): Response {
    const limit = Math.min(50, Math.max(1, Number(url.searchParams.get("limit")) || 12));
    const rows = this.db.all<Row>("SELECT id, at, slot, text, by_ai FROM ai_digest ORDER BY id DESC LIMIT ?", limit);
    return json(rows.map((r) => ({ id: Number(r.id), at: Number(r.at), slot: r.slot, text: r.text, byAi: !!r.by_ai })));
  }

  async cleanup(): Promise<{ photosExpired: number; opsPurged: number }> {
    const days = Math.max(1, Number(this.env.PHOTO_RETENTION_DAYS) || 90);
    const cutoff = Date.now() - days * DAY_MS;
    const old = this.db.all<Row>("SELECT photo_id, parcel_id FROM photos WHERE deleted_at = 0 AND keep = 0 AND taken_at < ? LIMIT 200", cutoff);
    for (const p of old) {
      await this.env.IMAGES.delete([`p/${p.photo_id}`, `t/${p.photo_id}`]);
      this.db.run("UPDATE photos SET deleted_at = ? WHERE photo_id = ?", Date.now(), p.photo_id as string);
      this.audit("system", "photo.expire", p.parcel_id as string, { photoId: p.photo_id }, null);
    }
    const purged = this.db.run("DELETE FROM ops_done WHERE at < ?", Date.now() - 7 * DAY_MS);
    this.flushQuota();
    return { photosExpired: old.length, opsPurged: purged };
  }
}

/** Chuẩn hóa mã để tra bảng `parcel_codes` (giống `normalize` của lõi). */
function normalizeForLookup(code: string): string {
  return code.replace(/[\s​﻿]+/gu, "").toUpperCase();
}

function diffOf(before: Parcel, after: Parcel, side: "before" | "after"): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  const b = before as unknown as Record<string, unknown>, a = after as unknown as Record<string, unknown>;
  for (const k of Object.keys(a)) if (JSON.stringify(b[k]) !== JSON.stringify(a[k])) out[k] = side === "before" ? b[k] : a[k];
  return out;
}

/** Trường do người dùng sửa trên cloud không bị nhập đè (cloud/API.md mục 7). */
function mergeImport(existing: Parcel, incoming: Parcel): Parcel {
  const m: Parcel = { ...incoming };
  m.userNote = existing.userNote;
  m.isWatchlist = existing.isWatchlist;
  m.watchlistReason = existing.watchlistReason;
  if (existing.photoId) m.photoId = existing.photoId;
  m.isArchived = existing.isArchived; m.archivedAt = existing.archivedAt;
  m.isDeleted = existing.isDeleted; m.deletedAt = existing.deletedAt; m.deletedFrom = existing.deletedFrom;
  if (existing.status === ReceiveStatus.received) { m.status = existing.status; m.recvDate = existing.recvDate; }
  return m;
}

/** Nhà cung cấp AI của cổng AI (đều theo chuẩn OpenAI). Dự phòng: Groq -> Google -> NVIDIA NIM -> Cloudflare Workers AI (chủ dự án 26/11/2026). */
type AiProvider = "groq" | "gemini" | "nvidia" | "cloudflare";
const AI_BASE: Record<Exclude<AiProvider, "cloudflare">, string> = {
  groq: "https://api.groq.com/openai/v1",
  gemini: "https://generativelanguage.googleapis.com/v1beta/openai",
  nvidia: "https://integrate.api.nvidia.com/v1",
};
