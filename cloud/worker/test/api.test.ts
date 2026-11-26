/// <reference types="@cloudflare/vitest-pool-workers/types" />
import { env, runInDurableObject, SELF } from "cloudflare:test";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Env } from "../src/index";
import type { ShopDO } from "../src/shop";

const E = env as unknown as Env;

const BASE = "https://w.example/v1";

async function call(method: string, path: string, body?: unknown, token?: string, extraHeaders: Record<string, string> = {}) {
  const headers: Record<string, string> = { ...extraHeaders };
  if (token) headers["Authorization"] = `Bearer ${token}`;
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const res = await SELF.fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await res.text();
  let data: any = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  return { status: res.status, data };
}

const parcel = (id: string, over: Record<string, unknown> = {}) => ({
  id, orderId: "O-" + id, track: "TRK-" + id, platform: "Shopee", orderType: "Trả hàng", status: "Chưa trả", price: 100000,
  items: [{ product: "Áo thun", color: "Đen", size: "M", qty: 1, returnedQty: 0 }], ...over,
});

let owner: string, phoneA: string, phoneB: string, pc: string;

async function pair(ownerToken: string, name: string, kind: "phone" | "pc"): Promise<string> {
  const start = await call("POST", "/pair/start", {}, ownerToken);
  expect(start.status).toBe(200);
  const done = await call("POST", "/pair/complete", { code: start.data.code, deviceName: name, kind });
  expect(done.status).toBe(200);
  return done.data.token as string;
}

async function importParcels(parcels: unknown[], key = "test-import-key") {
  return call("POST", "/import", { source: "sheet", signature: "sig1", parcels }, undefined, { "X-Import-Key": key });
}

const scan = (token: string, code: string, opId = crypto.randomUUID()) =>
  call("POST", "/ops", { ops: [{ opId, type: "scan", code }] }, token).then((r) => ({ ...r, res: r.data?.results?.[0] }));

async function stub() {
  return E.SHOP.get(E.SHOP.idFromName("main"));
}

const TABLES = ["import_issues", "import_log", "return_watch", "devices","pair_codes", "parcels", "parcel_codes", "ops_done", "audit_log", "scans", "photos", "import_state", "quota"];

// Trạng thái Durable Object được giữ giữa các test: xóa sạch bảng + ảnh trước mỗi test rồi tạo lại thiết bị.
beforeEach(async () => {
  const s = await stub();
  await runInDurableObject(s, (_i: ShopDO, state: DurableObjectState) => {
    for (const t of TABLES) state.storage.sql.exec(`DELETE FROM ${t}`);
    state.storage.sql.exec("UPDATE meta SET v = '0' WHERE k = 'rev'");
    state.storage.sql.exec("DELETE FROM meta WHERE k <> 'rev'");
  });
  const listed = await E.IMAGES.list();
  if (listed.objects.length) await E.IMAGES.delete(listed.objects.map((o: { key: string }) => o.key));
  const b = await call("POST", "/pair/bootstrap", { bootstrapKey: "test-bootstrap-key", deviceName: "Máy chủ", kind: "pc" });
  expect(b.status).toBe(200);
  owner = b.data.token;
  phoneA = await pair(owner, "Điện thoại A", "phone");
  phoneB = await pair(owner, "Điện thoại B", "phone");
  pc = await pair(owner, "Máy tính 1", "pc");
});

describe("ghép thiết bị và quyền", () => {
  it("khóa khởi tạo chỉ dùng được một lần và phải đúng", async () => {
    const again = await call("POST", "/pair/bootstrap", { bootstrapKey: "test-bootstrap-key", deviceName: "X", kind: "pc" });
    expect(again.status).toBe(403);
  });

  it("mã ghép dùng một lần, sai mã bị từ chối, token cho đúng vai trò", async () => {
    const start = await call("POST", "/pair/start", {}, owner);
    const code = start.data.code as string;
    expect(code).toMatch(/^\d{6}$/);
    const bad = await call("POST", "/pair/complete", { code: code === "000000" ? "111111" : "000000", deviceName: "Sai", kind: "phone" });
    expect(bad.status).toBe(401);
    const ok = await call("POST", "/pair/complete", { code, deviceName: "Máy mới", kind: "phone" });
    expect(ok.status).toBe(200);
    expect(ok.data.role).toBe("staff");
    const reuse = await call("POST", "/pair/complete", { code, deviceName: "Máy khác", kind: "phone" });
    expect(reuse.status).toBe(401);
    const me = await call("GET", "/me", undefined, ok.data.token);
    expect(me.data).toMatchObject({ deviceName: "Máy mới", role: "staff" });
  });

  it("mật khẩu cửa hàng: điện thoại tự ghép không cần máy tính, mỗi máy một token riêng, vai trò nhân viên", async () => {
    const a1 = await call("POST", "/pair/join", { joinKey: "mat-khau-cua-hang-gia", deviceName: "iPhone kho 1", kind: "phone" });
    expect(a1.status).toBe(200);
    expect(a1.data.role).toBe("staff");
    const a2 = await call("POST", "/pair/join", { joinKey: "mat-khau-cua-hang-gia", deviceName: "iPhone kho 2", kind: "phone" });
    expect(a2.data.token).not.toBe(a1.data.token);
    expect((await call("GET", "/me", undefined, a1.data.token)).data).toMatchObject({ deviceName: "iPhone kho 1", role: "staff" });
    // thu hồi một máy không ảnh hưởng máy kia
    await call("DELETE", "/devices/" + encodeURIComponent(a1.data.deviceId), undefined, owner);
    expect((await call("GET", "/me", undefined, a1.data.token)).status).toBe(401);
    expect((await call("GET", "/me", undefined, a2.data.token)).status).toBe(200);
  });

  it("mật khẩu cửa hàng sai bị từ chối và sai nhiều lần thì khóa tạm (kể cả khi nhập đúng)", async () => {
    const bad = await call("POST", "/pair/join", { joinKey: "sai", deviceName: "Lạ", kind: "phone" });
    expect(bad.status).toBe(401);
    expect(JSON.stringify(bad.data)).not.toContain("mat-khau");
    for (let i = 0; i < 9; i++) await call("POST", "/pair/join", { joinKey: "sai" + i, deviceName: "Lạ", kind: "phone" });
    const locked = await call("POST", "/pair/join", { joinKey: "mat-khau-cua-hang-gia", deviceName: "Lạ", kind: "phone" });
    expect(locked.status).toBe(429);
  });

  it("nhân viên không tạo mã ghép, không xem/thu hồi thiết bị", async () => {
    expect((await call("POST", "/pair/start", {}, phoneA)).status).toBe(403);
    expect((await call("GET", "/devices", undefined, phoneA)).status).toBe(403);
    const list = await call("GET", "/devices", undefined, owner);
    expect(list.data).toHaveLength(4);
    expect(JSON.stringify(list.data)).not.toContain(phoneA); // không lộ token
  });

  it("thu hồi thiết bị thì token ngừng dùng được ngay", async () => {
    const list = await call("GET", "/devices", undefined, owner);
    const a = list.data.find((d: any) => d.deviceName === "Điện thoại A");
    expect((await call("GET", "/me", undefined, phoneA)).status).toBe(200);
    expect((await call("DELETE", `/devices/${a.deviceId}`, undefined, owner)).status).toBe(200);
    expect((await call("GET", "/me", undefined, phoneA)).status).toBe(401);
  });

  it("không token hoặc token sai thì 401", async () => {
    expect((await call("GET", "/sync")).status).toBe(401);
    expect((await call("GET", "/sync", undefined, "khong-co")).status).toBe(401);
  });
});

describe("nhập đơn", () => {
  it("từ chối khóa cấm (dữ liệu người mua/dữ liệu thô), nhận phần còn lại", async () => {
    const r = await importParcels([
      parcel("p1"),
      parcel("p2", { buyerNote: "khách tên A" }),
      parcel("p3", { rawDetails: { deliveryAddress: "x" } }),
      parcel("p4", { extra: { returnReason: "Sai mô tả", deliveryAddress: "x" } }),
    ]);
    expect(r.status).toBe(200);
    expect(r.data.added).toBe(1);
    expect(r.data.rejected.map((x: any) => x.id).sort()).toEqual(["p2", "p3", "p4"]);
  });

  it("nhập lại cùng nội dung thì không ghi lại; đổi nội dung thì cập nhật", async () => {
    const hash = "h1";
    expect((await importParcels([parcel("p1", { hash })])).data).toMatchObject({ added: 1, updated: 0, unchanged: 0 });
    const rev1 = (await call("GET", "/sync?since=0", undefined, owner)).data.rev;
    expect((await importParcels([parcel("p1", { hash })])).data).toMatchObject({ added: 0, updated: 0, unchanged: 1 });
    expect((await call("GET", "/sync?since=0", undefined, owner)).data.rev).toBe(rev1);
    expect((await importParcels([parcel("p1", { hash: "h2", price: 5 })])).data).toMatchObject({ updated: 1 });
  });

  it("Sheet ghi vào kiện thì máy tính gửi lại CÙNG bản cũ vẫn được áp dụng (không bị coi là 'không đổi')", async () => {
    await importParcels([parcel("p1", { hash: "h1", platformStatus: "Đã huỷ", orderType: "Đơn hủy" })]);
    const r = await call("POST", "/import/sheet", { signature: "s", parcels: [{ platform: "Shopee", orderId: "O-p1", track: "TRK-p1",
      orderType: "Trả hàng/Hoàn tiền", platformStatus: "Chờ xác nhận", items: [{ product: "Áo thun", color: "Đen", size: "M", qty: 1, returnedQty: 1 }] }] },
      undefined, { "X-Import-Key": "test-import-key" });
    expect(r.data.updated).toBe(1);
    const again = await importParcels([parcel("p1", { hash: "h1", platformStatus: "Đã huỷ", orderType: "Đơn hủy" })]);
    expect(again.data).toMatchObject({ updated: 1, unchanged: 0 });
    const p = (await call("GET", "/sync?since=0", undefined, owner)).data.changes.find((c: any) => c.type === "parcel").parcel;
    expect(p).toMatchObject({ platformStatus: "Đã huỷ", orderType: "Đơn hủy" });
  });

  it("khóa nhập sai thì 401; token thiết bị cũng nhập được", async () => {
    expect((await importParcels([parcel("p1")], "sai")).status).toBe(401);
    const r = await call("POST", "/import", { source: "file", parcels: [parcel("p9")] }, phoneA);
    expect(r.status).toBe(200);
  });

  it("nhập lại không đè ghi chú/theo dõi người dùng sửa và không hoàn tác kiện đã nhận", async () => {
    await importParcels([parcel("p1", { hash: "a" })]);
    await call("POST", "/ops", { ops: [
      { opId: crypto.randomUUID(), type: "editNote", id: "p1", userNote: "ghi chú của tôi" },
      { opId: crypto.randomUUID(), type: "setWatch", id: "p1", on: true, reason: "theo dõi" },
    ] }, owner);
    await scan(phoneA, "TRK-p1");
    await importParcels([parcel("p1", { hash: "b", status: "Chưa trả", userNote: "", price: 7 })]);
    const p = (await call("GET", "/sync?since=0", undefined, owner)).data.changes[0].parcel;
    expect(p).toMatchObject({ userNote: "ghi chú của tôi", isWatchlist: true, status: "Hàng đã trả", price: 7 });
    expect(p.recvDate).toMatch(/^\d{2}\/\d{2}\/\d{4} \d{2}:\d{2}:\d{2}$/);
  });

  it("trạng thái nhập đơn được ghi nhớ để nguồn bỏ qua vòng không đổi", async () => {
    await importParcels([parcel("p1")]);
    const st = await call("GET", "/import/state?source=sheet", undefined, undefined, { "X-Import-Key": "test-import-key" });
    expect(st.data).toMatchObject({ signature: "sig1", parcelCount: 1 });
  });
});

describe("đồng bộ phần thay đổi", () => {
  it("lấy từ rev, phân trang, không trả lại phần đã có", async () => {
    await importParcels([parcel("a"), parcel("b"), parcel("c")]);
    const all = await call("GET", "/sync?since=0&limit=2", undefined, phoneA);
    expect(all.data.changes).toHaveLength(2);
    expect(all.data.more).toBe(true);
    const rest = await call("GET", `/sync?since=${all.data.rev}&limit=2`, undefined, phoneA);
    expect(rest.data.changes).toHaveLength(1);
    expect(rest.data.more).toBe(false);
    const none = await call("GET", `/sync?since=${rest.data.rev}`, undefined, phoneA);
    expect(none.data.changes).toHaveLength(0);
  });
});

describe("quét nhận kiện", () => {
  it("nhận kiện: ack, ghi trạng thái + giờ nhận, máy khác thấy trong /sync", async () => {
    await importParcels([parcel("p1")]);
    const r = await scan(phoneA, "  trk-p1 ");
    expect(r.res.ok).toBe(true);
    expect(r.res.result).toMatchObject({ outcome: "received", ack: true, receivedBy: "Điện thoại A" });
    const sync = await call("GET", "/sync?since=0", undefined, phoneB);
    expect(sync.data.changes[0].parcel.status).toBe("Hàng đã trả");
  });

  it("hai máy cùng quét một kiện: chỉ máy đến trước nhận, máy sau nhận 'đã nhận trước đó' kèm tên máy", async () => {
    await importParcels([parcel("p1")]);
    const [a, b] = await Promise.all([scan(phoneA, "TRK-p1"), scan(phoneB, "TRK-p1")]);
    const outcomes = [a.res.result.outcome, b.res.result.outcome].sort();
    expect(outcomes).toEqual(["alreadyReceived", "received"]);
    const loser = a.res.result.outcome === "alreadyReceived" ? a : b;
    expect(["Điện thoại A", "Điện thoại B"]).toContain(loser.res.result.receivedBy);
    expect(loser.res.result.receivedBy).toBe(loser === a ? "Điện thoại B" : "Điện thoại A");   // tên máy đã nhận TRƯỚC
  });

  it("nhận kiện A không nhận kiện B cùng mã đơn; mã đơn chung -> mơ hồ, không ack", async () => {
    await importParcels([parcel("a", { orderId: "O1", track: "A" }), parcel("b", { orderId: "O1", track: "B" })]);
    expect((await scan(phoneA, "A")).res.result.outcome).toBe("received");
    const amb = await scan(phoneA, "O1");
    expect(amb.res.result).toMatchObject({ outcome: "ambiguous", ack: false });
    const sync = (await call("GET", "/sync?since=0", undefined, owner)).data.changes;
    expect(sync.find((c: any) => c.parcel.id === "b").parcel.status).toBe("Chưa trả");
  });

  it("không tìm thấy, cần kiểm tra thủ công, kiện đã lưu trữ/xóa không được quét", async () => {
    await importParcels([parcel("n", { status: "Không cần nhận" }), parcel("z")]);
    expect((await scan(phoneA, "NOPE")).res.result).toMatchObject({ outcome: "notFound", ack: false });
    expect((await scan(phoneA, "TRK-n")).res.result).toMatchObject({ outcome: "needsManualCheck", ack: false });
    await call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "archive", id: "z" }] }, owner);
    expect((await scan(phoneA, "TRK-z")).res.result.outcome).toBe("notFound");
  });

  it("gửi lặp cùng opId trả đúng kết quả cũ, không làm hai lần", async () => {
    await importParcels([parcel("p1")]);
    const opId = crypto.randomUUID();
    const first = await scan(phoneA, "TRK-p1", opId);
    const rev = (await call("GET", "/sync?since=0", undefined, owner)).data.rev;
    const second = await scan(phoneB, "TRK-p1", opId);
    expect(second.res).toEqual(first.res);
    expect((await call("GET", "/sync?since=0", undefined, owner)).data.rev).toBe(rev);
  });
});

describe("thao tác khác", () => {
  it("đổi trạng thái có kiểm tra trạng thái mong đợi (xung đột thì 'conflict')", async () => {
    await importParcels([parcel("p1")]);
    const ok = await call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "setStatus", id: "p1", status: "Bị mất", expectedStatus: "Chưa trả" }] }, owner);
    expect(ok.data.results[0].ok).toBe(true);
    const bad = await call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "setStatus", id: "p1", status: "Chưa trả", expectedStatus: "Chưa trả" }] }, owner);
    expect(bad.data.results[0]).toMatchObject({ ok: false, error: { code: "conflict" } });
    const invalid = await call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "setStatus", id: "p1", status: "Bậy bạ" }] }, owner);
    expect(invalid.data.results[0].ok).toBe(false);
  });

  it("xóa mềm rồi khôi phục; ghi chú dài quá bị từ chối; ô không cho sửa bị từ chối", async () => {
    await importParcels([parcel("p1")]);
    const run = (op: Record<string, unknown>) => call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), ...op }] }, owner).then((r) => r.data.results[0]);
    expect((await run({ type: "delete", id: "p1" })).ok).toBe(true);
    let p = (await call("GET", "/sync?since=0", undefined, owner)).data.changes[0].parcel;
    expect(p).toMatchObject({ isDeleted: true, deletedFrom: "Bảng chính" });
    expect((await run({ type: "restore", id: "p1" })).ok).toBe(true);
    p = (await call("GET", "/sync?since=0", undefined, owner)).data.changes[0].parcel;
    expect(p.isDeleted).toBe(false);
    expect((await run({ type: "editNote", id: "p1", userNote: "x".repeat(2001) })).ok).toBe(false);
    expect((await run({ type: "editCell", id: "p1", field: "id", value: "khac" })).ok).toBe(false);
    expect((await run({ type: "editCell", id: "p1", field: "carrier", value: "GHN" })).ok).toBe(true);
  });

  it("mọi thay đổi ghi nhật ký (trước/sau), không mất dấu", async () => {
    await importParcels([parcel("p1")]);
    await call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "editNote", id: "p1", userNote: "mới" }] }, owner);
    const s = await stub();
    const rows = await runInDurableObject(s, (_i: ShopDO, state: DurableObjectState) =>
      state.storage.sql.exec("SELECT action, before, after FROM audit_log WHERE parcel_id = 'p1'").toArray());
    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({ action: "editNote" });
    expect(JSON.parse(rows[0]!.after as string)).toEqual({ userNote: "mới" });
  });
});

describe("Bảng Hàng Hoàn Về", () => {
  const watchChanges = async (since = 0) => ((await call("GET", `/sync?since=${since}`, undefined, owner)).data.changes as any[]).filter((c) => c.type === "returnWatch");

  it("đơn 'Đã đồng ý' chưa nhận tự vào bảng một lần; đơn đã nhận/lưu trữ/không cần nhận thì không", async () => {
    await importParcels([
      parcel("a", { platformStatus: "Đã đồng ý" }),
      parcel("b", { platformStatus: "Đã đồng ý", status: "Hàng đã trả" }),
      parcel("c", { platformStatus: "Đã đồng ý", status: "Không cần nhận" }),
      parcel("d", { platformStatus: "Đã đồng ý", isArchived: true }),
      parcel("e", { platformStatus: "Đang xử lý" }),
      parcel("t", { platform: "TikTok", platformStatus: "Chờ khách gửi hàng" }),
    ]);
    const w = await watchChanges();
    expect(w.map((c) => c.key).sort()).toEqual(["a", "t"]);
    expect(w[0]).toMatchObject({ off: false });
    expect(w[0].since).toBeGreaterThan(1_700_000_000_000);

    // nhập lại không tạo dòng mới / không đổi mốc vào bảng
    const before = w.find((c) => c.key === "a").since;
    await importParcels([parcel("a", { platformStatus: "Đã đồng ý", price: 5, hash: "x2" })]);
    const after = await watchChanges();
    expect(after).toHaveLength(2);
    expect(after.find((c) => c.key === "a").since).toBe(before);
  });

  it("bỏ khỏi bảng rồi khôi phục (không xóa đơn); bỏ rồi nhập lại không tự vào lại; đơn ngoài bảng thì 404", async () => {
    await importParcels([parcel("a", { platformStatus: "Đã đồng ý" }), parcel("z")]);
    const run = (op: Record<string, unknown>) => call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), ...op }] }, phoneA).then((r) => r.data.results[0]);
    expect((await run({ type: "rwRemove", id: "a" })).ok).toBe(true);
    expect((await watchChanges()).find((c) => c.key === "a").off).toBe(true);
    await importParcels([parcel("a", { platformStatus: "Đã đồng ý", price: 9, hash: "x3" })]);
    expect((await watchChanges()).find((c) => c.key === "a").off).toBe(true);          // vẫn bị bỏ
    expect((await run({ type: "rwRestore", id: "a" })).ok).toBe(true);
    expect((await watchChanges()).find((c) => c.key === "a").off).toBe(false);
    const nf = await run({ type: "rwRemove", id: "z" });
    expect(nf).toMatchObject({ ok: false, error: { code: "not_found" } });
    const p = (await call("GET", "/sync?since=0", undefined, owner)).data.changes.find((c: any) => c.type === "parcel" && c.parcel.id === "a");
    expect(p.parcel.isDeleted).toBe(false);
  });

  it("nhập dấu của dữ liệu cũ: chỉ thêm khi chưa có, giữ cờ đã bỏ, bỏ qua kiện không tồn tại", async () => {
    await importParcels([parcel("a"), parcel("b")]);
    const r = await call("POST", "/import", { source: "file", signature: "s", parcels: [], returnWatch: [
      { id: "a", since: 1_700_000_000_123, off: true }, { id: "b", since: 1_700_000_000_456, off: false }, { id: "khongco", since: 1 },
    ] }, owner);
    expect(r.data.returnWatchAdded).toBe(2);
    const again = await call("POST", "/import", { source: "file", signature: "s", parcels: [], returnWatch: [{ id: "a", since: 5, off: false }] }, owner);
    expect(again.data.returnWatchAdded).toBe(0);
    const w = await watchChanges();
    expect(w.find((c) => c.key === "a")).toMatchObject({ since: 1_700_000_000_123, off: true });
    expect(w.find((c) => c.key === "b")).toMatchObject({ since: 1_700_000_000_456, off: false });
  });

  it("dấu cloud TỰ thêm lúc nhập được thay bằng giờ gốc của dữ liệu cũ; dấu người dùng đã bỏ/khôi phục thì không", async () => {
    await importParcels([parcel("a", { platformStatus: "Đã đồng ý" }), parcel("b", { platformStatus: "Đã đồng ý" })]);
    const auto = await watchChanges();
    expect(auto.map((c) => c.key).sort()).toEqual(["a", "b"]);               // tự thêm, giờ = lúc nhập
    expect((await call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "rwRemove", id: "b" }] }, phoneA)).data.results[0].ok).toBe(true);
    const r = await call("POST", "/import", { source: "file", signature: "s", parcels: [], returnWatch: [
      { id: "a", since: 1_700_000_000_123, off: false }, { id: "b", since: 1_700_000_000_456, off: false },
    ] }, owner);
    expect(r.data).toMatchObject({ returnWatchAdded: 0, returnWatchFixed: 1 });
    const w = await watchChanges();
    expect(w.find((c) => c.key === "a")).toMatchObject({ since: 1_700_000_000_123, off: false });
    expect(w.find((c) => c.key === "b")).toMatchObject({ off: true });                  // người dùng đã bỏ: giữ nguyên
    expect(w.find((c) => c.key === "b").since).not.toBe(1_700_000_000_456);
    // gửi lại lần nữa: "a" giờ là dấu gốc, không đổi nữa
    const again = await call("POST", "/import", { source: "file", signature: "s", parcels: [], returnWatch: [{ id: "a", since: 5, off: true }] }, owner);
    expect(again.data).toMatchObject({ returnWatchAdded: 0, returnWatchFixed: 0 });
  });

  it("phân trang trộn kiện + bảng hoàn về theo rev: không sót, không lặp", async () => {
    await importParcels([parcel("a", { platformStatus: "Đã đồng ý" }), parcel("b", { platformStatus: "Đã đồng ý" }), parcel("c")]);
    const seen: string[] = [];
    let since = 0;
    for (let i = 0; i < 20; i++) {
      const page = (await call("GET", `/sync?since=${since}&limit=2`, undefined, owner)).data;
      for (const c of page.changes) seen.push(c.type === "parcel" ? "p:" + c.parcel.id : "w:" + c.key);
      since = page.rev;
      if (!page.more) break;
    }
    expect(seen.sort()).toEqual(["p:a", "p:b", "p:c", "w:a", "w:b"]);
  });
});

describe("nhập từ Google Sheet (cloud tự đối chiếu, cùng bộ gộp với máy tính)", () => {
  const sheetParcel = (over: Record<string, unknown> = {}) => ({
    platform: "Shopee", orderId: "261120ABCDEF", returnOrderId: "RR1", track: "SPXVN000000101", orderType: "Trả hàng/Hoàn tiền",
    platformStatus: "Đã đồng ý", price: 150000, priceHasValue: true, returnDate: "20/11/2026", buyerNote: "Nguyễn Văn A 0912345678",
    rawDetails: { returnReason: "Sai mô tả", deliveryAddress: "12 Đường Giả" },
    items: [{ product: "Áo thun", color: "Đen", size: "M", qty: 1, returnedQty: 1 }], ...over,
  });
  const sendSheet = (parcels: unknown[]) =>
    call("POST", "/import/sheet", { signature: "s1", parcels }, undefined, { "X-Import-Key": "test-import-key" });
  const all = async () => ((await call("GET", "/sync?since=0", undefined, owner)).data.changes as any[]).filter((c) => c.type === "parcel").map((c) => c.parcel);

  it("kiện mới được tạo với id mới, mặc định như nạp file; KHÔNG lưu ghi chú người mua/địa chỉ", async () => {
    const r = await sendSheet([sheetParcel()]);
    expect(r.status).toBe(200);
    expect(r.data).toMatchObject({ added: 1, updated: 0, written: 1 });
    const [p] = await all();
    expect(p).toMatchObject({ orderId: "261120ABCDEF", track: "SPXVN000000101", status: "Chưa trả", carrier: "SPX Express", platformStatus: "Đã đồng ý", price: 150000 });
    expect(p.id).toMatch(/^s_/);
    expect(p.extra.returnReason).toBe("Sai mô tả");
    const text = JSON.stringify(p);
    expect(text).not.toContain("0912345678");
    expect(text).not.toContain("12 Đường Giả");
    expect(text).not.toContain("buyerNote");
  });

  it("nhập lại cùng kiện thì CẬP NHẬT (không tạo dòng mới, không cộng lặp sản phẩm) và giữ trường người dùng/kho", async () => {
    await sendSheet([sheetParcel()]);
    const [p0] = await all();
    await call("POST", "/ops", { ops: [
      { opId: crypto.randomUUID(), type: "editNote", id: p0.id, userNote: "của tôi" },
      { opId: crypto.randomUUID(), type: "setWatch", id: p0.id, on: true, reason: "theo dõi" },
    ] }, owner);
    await scan(phoneA, "SPXVN000000101");
    const r = await sendSheet([sheetParcel({ platformStatus: "Đã hoàn tiền", price: 160000 })]);
    expect(r.data).toMatchObject({ added: 0, updated: 1 });
    const ps = await all();
    expect(ps).toHaveLength(1);
    expect(ps[0]).toMatchObject({ platformStatus: "Đã hoàn tiền", price: 160000, userNote: "của tôi", isWatchlist: true, status: "Hàng đã trả" });
    expect(ps[0].items).toHaveLength(1);
    expect(ps[0].items[0].qty).toBe(1);
  });

  it("kiện A/B cùng mã đơn khác mã vận đơn là hai dòng; mã chung khớp nhiều kiện -> KHÔNG đổi, ghi vào danh sách cần kiểm tra", async () => {
    await sendSheet([sheetParcel({ returnOrderId: "", track: "SPXVN000000201" }), sheetParcel({ returnOrderId: "", track: "SPXVN000000202" })]);
    expect(await all()).toHaveLength(2);
    // hai kiện cùng liên kết tường minh tới một mã -> mã đó khớp cả hai
    const ps = await all();
    for (const p of ps) await call("POST", "/import", { source: "file", parcels: [{ ...p, trackingCodes: ["SPXVN000000299"], rev: undefined, updatedAt: undefined }] }, owner);
    const r = await sendSheet([sheetParcel({ returnOrderId: "", track: "SPXVN000000299" })]);
    expect(r.data.ambiguous).toHaveLength(1);
    expect(r.data.ambiguous[0].candidates).toBe(2);
    expect(await all()).toHaveLength(2);
    const issues = await call("GET", "/import/issues", undefined, undefined, { "X-Import-Key": "test-import-key" });
    expect(issues.data[0]).toMatchObject({ source: "sheet", candidates: 2 });
  });

  it("lịch sử đồng bộ: các lô gửi liền nhau gộp thành MỘT lượt, có số thêm/cập nhật; máy nào cũng xem được (token thiết bị)", async () => {
    await sendSheet([sheetParcel({ returnOrderId: "", track: "SPXVN000000401" })]);
    await sendSheet([sheetParcel({ returnOrderId: "", track: "SPXVN000000402" }), sheetParcel({ returnOrderId: "", track: "SPXVN000000401", platformStatus: "Đã về shop" })]);
    await call("POST", "/import", { source: "file", parcels: [] }, owner);
    expect((await call("GET", "/import/log", undefined, undefined)).status).toBe(401);
    const log = await call("GET", "/import/log", undefined, phoneA);
    expect(log.status).toBe(200);
    const sheet = (log.data as any[]).filter((r) => r.source === "sheet");
    expect(sheet).toHaveLength(1);
    expect(sheet[0]).toMatchObject({ batches: 2, sent: 3, added: 2, updated: 1, ambiguous: 0 });
    expect(sheet[0].end).toBeGreaterThanOrEqual(sheet[0].start);
    expect((log.data as any[]).some((r) => r.source === "file")).toBe(true);
  });

  it("kiện đã xóa thì bỏ qua (không nạp lại); kiện ở kho lưu trữ chỉ được bổ sung, không thành dòng mới", async () => {
    await sendSheet([sheetParcel({ track: "SPXVN000000301", returnOrderId: "C301" }), sheetParcel({ orderId: "261121XYZ", track: "SPXVN000000302", returnOrderId: "C302" })]);
    const [a, b] = await all();
    await call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "delete", id: a.id }, { opId: crypto.randomUUID(), type: "archive", id: b.id }] }, owner);
    const r = await sendSheet([
      sheetParcel({ track: "SPXVN000000301", returnOrderId: "C301", platformStatus: "Khác" }),
      sheetParcel({ orderId: "261121XYZ", track: "SPXVN000000302", returnOrderId: "C302", platformStatus: "Đã hoàn tiền" }),
    ]);
    expect(r.data).toMatchObject({ added: 0, skippedDeleted: 1, enrichedArchived: 1 });
    const ps = await all();
    expect(ps).toHaveLength(2);
    expect(ps.find((p) => p.id === b.id)).toMatchObject({ isArchived: true, platformStatus: "Đã hoàn tiền" });
    expect(ps.find((p) => p.id === a.id)).toMatchObject({ isDeleted: true, platformStatus: "Đã đồng ý" });
  });

  it("khóa nhập sai thì 401; kiện thiếu mọi mã bị loại; quá 100 kiện bị từ chối", async () => {
    expect((await call("POST", "/import/sheet", { parcels: [] }, undefined, { "X-Import-Key": "sai" })).status).toBe(401);
    const r = await sendSheet([{ platform: "Shopee" }, sheetParcel()]);
    expect(r.data).toMatchObject({ rejected: 1, added: 1 });
    expect((await sendSheet(Array.from({ length: 101 }, () => sheetParcel()))).status).toBe(422);
  });

  it("Shopee: kiện cũ chưa có mã vận đơn cùng mã đơn được điền mã từ Sheet (đúng một kiện)", async () => {
    await importParcels([parcel("p1", { orderId: "SPT1", track: "", platform: "Shopee", platformStatus: "Đang xử lý" })]);
    const r = await sendSheet([sheetParcel({ orderId: "SPT1", returnOrderId: "", track: "SPXVN000000777" })]);
    expect(r.status).toBe(200);
    expect(r.data).toMatchObject({ added: 0, updated: 1 });
    const ps = await all();
    expect(ps).toHaveLength(1);
    expect(ps[0]).toMatchObject({ id: "p1", track: "SPXVN000000777" });
  });

  it("chủ shop xóa hẳn kiện Sheet CHƯA ai đụng; kiện đã quét/sửa/dữ liệu cũ giữ nguyên; nhân viên không làm được", async () => {
    await importParcels([parcel("p1")]);                               // dữ liệu cũ (không phải s_)
    await sendSheet([
      sheetParcel({ orderId: "SPA", returnOrderId: "RA", track: "SPXVN000000901" }),
      sheetParcel({ orderId: "SPB", returnOrderId: "RB", track: "SPXVN000000902" }),
      sheetParcel({ orderId: "SPC", returnOrderId: "RC", track: "SPXVN000000903" }),
    ]);
    expect((await scan(phoneA, "SPXVN000000902")).res.ok).toBe(true);  // B đã quét
    const c = (await all()).find((p) => p.orderId === "SPC");
    await call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "editNote", id: c.id, userNote: "đã gọi khách" }] }, phoneA);  // C đã sửa

    const body = { confirm: "XOA_KIEN_SHEET_CHUA_DUNG" };
    expect((await call("POST", "/admin/purge-fresh-sheet", body, phoneA)).status).toBe(403);
    expect((await call("POST", "/admin/purge-fresh-sheet", {}, owner)).status).toBe(422);
    const dry = await call("POST", "/admin/purge-fresh-sheet", { ...body, dryRun: true }, owner);
    expect(dry.data).toMatchObject({ dryRun: true, purged: 0, wouldPurge: 1, kept: 2 });
    expect(await all()).toHaveLength(4);

    const r = await call("POST", "/admin/purge-fresh-sheet", body, owner);
    expect(r.data).toMatchObject({ purged: 1, kept: 2 });
    const left = await all();
    expect(left.map((p) => p.orderId).sort()).toEqual(["O-p1", "SPB", "SPC"]);
    expect((await scan(phoneA, "SPXVN000000901")).res.result.outcome).toBe("notFound");   // mã của kiện đã xóa không còn khớp
    // Sheet gửi lại A thì tạo lại bình thường
    expect((await sendSheet([sheetParcel({ orderId: "SPA", returnOrderId: "RA", track: "SPXVN000000901" })])).data).toMatchObject({ added: 1 });
  });
});

describe("lịch sử sửa", () => {
  it("trả nhật ký trước/sau của kiện, kèm tên máy; sửa lý do theo dõi qua editCell", async () => {
    await importParcels([parcel("p1")]);
    await call("POST", "/ops", { ops: [
      { opId: crypto.randomUUID(), type: "editNote", id: "p1", userNote: "một" },
      { opId: crypto.randomUUID(), type: "editCell", id: "p1", field: "watchlistReason", value: "khách khiếu nại" },
    ] }, phoneA);
    const h = await call("GET", "/parcels/p1/history", undefined, phoneB);
    expect(h.status).toBe(200);
    expect(h.data).toHaveLength(2);
    expect(h.data[0]).toMatchObject({ action: "editCell", deviceName: "Điện thoại A", after: { watchlistReason: "khách khiếu nại" } });
    expect(h.data[1].before).toEqual({ userNote: "" });
  });
});

describe("ảnh", () => {
  const jpeg = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4, 5]);
  const put = (token: string, id: string, qs: string, bytes: Uint8Array = jpeg) =>
    SELF.fetch(`${BASE}/photos/${id}?${qs}`, { method: "PUT", headers: { Authorization: `Bearer ${token}` }, body: bytes });

  it("điện thoại vừa quét đúng một kiện thì tải ảnh lên được; máy khác xem được; có trong danh sách ảnh của kiện", async () => {
    await importParcels([parcel("p1")]);
    await scan(phoneA, "TRK-p1");
    const up = await put(phoneA, "photo_0001", "parcelId=p1&mime=image/jpeg");
    expect(up.status).toBe(200);
    const th = await put(phoneA, "photo_0001", "parcelId=p1&mime=image/jpeg&kind=thumb", new Uint8Array([9, 9]));
    expect(th.status).toBe(200);
    const full = await SELF.fetch(`${BASE}/photos/photo_0001`, { headers: { Authorization: `Bearer ${pc}` } });
    expect(new Uint8Array(await full.arrayBuffer())).toEqual(jpeg);
    expect(full.headers.get("Cache-Control")).toContain("max-age");
    const thumb = await SELF.fetch(`${BASE}/photos/photo_0001?size=thumb`, { headers: { Authorization: `Bearer ${pc}` } });
    expect(new Uint8Array(await thumb.arrayBuffer())).toEqual(new Uint8Array([9, 9]));
    const list = await call("GET", "/parcels/p1/photos", undefined, pc);
    expect(list.data).toHaveLength(1);
    expect(list.data[0]).toMatchObject({ photoId: "photo_0001", deviceName: "Điện thoại A", expired: false });
    const sync = (await call("GET", "/sync?since=0", undefined, pc)).data.changes[0].parcel;
    expect(sync.photoId).toBe("photo_0001");
  });

  it("máy tính không tải ảnh lên; chưa quét xác nhận hoặc quét bởi máy khác thì 409; quét mơ hồ thì không giữ ảnh", async () => {
    await importParcels([parcel("p1"), parcel("a", { orderId: "O9", track: "A" }), parcel("b", { orderId: "O9", track: "B" })]);
    await scan(phoneA, "TRK-p1");
    expect((await put(pc, "photo_pc01", "parcelId=p1")).status).toBe(403);
    expect((await put(phoneB, "photo_b001", "parcelId=p1")).status).toBe(409);   // máy B chưa quét kiện này
    await scan(phoneA, "O9");   // mơ hồ
    expect((await put(phoneA, "photo_a001", "parcelId=a")).status).toBe(409);
    expect((await put(phoneA, "photo_x001", "parcelId=khongco")).status).toBe(404);
    expect((await put(phoneA, "bad id!", "parcelId=p1")).status).toBe(422);
  });

  it("chuyển ảnh CŨ (legacy): chỉ chủ; không cần vừa quét; giữ giờ chụp gốc; ảnh quá hạn bị từ chối; không đè ảnh mới nhất của kiện", async () => {
    await importParcels([parcel("p1"), parcel("p2")]);
    expect((await put(phoneA, "legacy_001", "parcelId=p1&legacy=1")).status).toBe(403);          // nhân viên không được
    const recent = Date.now() - 5 * 86_400_000;
    expect((await put(owner, "legacy_001", `parcelId=p1&legacy=1&takenAt=${recent}`)).status).toBe(200);
    const sync = (await call("GET", "/sync?since=0", undefined, owner)).data.changes.find((c: any) => c.type === "parcel" && c.parcel.id === "p1");
    expect(sync.parcel.photoId).toBe("legacy_001");                                                // kiện chưa có ảnh: gắn
    const s = await stub();
    const row = await runInDurableObject(s, (_i: ShopDO, state: DurableObjectState) => state.storage.sql.exec("SELECT taken_at FROM photos WHERE photo_id='legacy_001'").one());
    expect(row.taken_at).toBe(recent);

    const tooOld = Date.now() - 200 * 86_400_000;
    const old = await put(owner, "legacy_002", `parcelId=p1&legacy=1&takenAt=${tooOld}`);
    expect(old.status).toBe(422);
    expect((await old.json() as any).error.code).toBe("expired");

    // ảnh cũ thứ hai không đè ảnh đang gắn của kiện
    expect((await put(owner, "legacy_003", `parcelId=p1&legacy=1&takenAt=${recent}`)).status).toBe(200);
    const again = (await call("GET", "/sync?since=0", undefined, owner)).data.changes.find((c: any) => c.type === "parcel" && c.parcel.id === "p1");
    expect(again.parcel.photoId).toBe("legacy_001");
    expect((await call("GET", "/parcels/p1/photos", undefined, owner)).data).toHaveLength(2);
    // không có takenAt: coi như mới chụp
    expect((await put(owner, "legacy_004", "parcelId=p2&legacy=1")).status).toBe(200);
  });

  it("chuyển ảnh cũ: kiện còn trỏ mã ảnh của MÁY TÍNH (không có trên cloud) thì gắn ảnh vừa chuyển; mã đã có trên cloud thì giữ", async () => {
    await importParcels([parcel("p1", { photoId: "ph-maytinh" }), parcel("p2", { photoId: "ph-maytinh2" })]);
    const recent = Date.now() - 86_400_000;
    expect((await put(owner, "legacy_101", `parcelId=p1&legacy=1&takenAt=${recent}`)).status).toBe(200);
    expect((await put(owner, "legacy_102", `parcelId=p1&legacy=1&takenAt=${recent}`)).status).toBe(200);
    const ps = (await call("GET", "/sync?since=0", undefined, owner)).data.changes.filter((c: any) => c.type === "parcel").map((c: any) => c.parcel);
    expect(ps.find((p: any) => p.id === "p1").photoId).toBe("legacy_101");        // ảnh đầu tiên (ảnh chính trên máy tính) được gắn
    expect(ps.find((p: any) => p.id === "p2").photoId).toBe("ph-maytinh2");       // chưa chuyển ảnh: giữ nguyên
    // máy tính đẩy lại đơn (vẫn mang mã ảnh cũ) không làm mất ảnh đã gắn
    await importParcels([parcel("p1", { photoId: "ph-maytinh", hash: "lai" })]);
    const p1 = (await call("GET", "/sync?since=0", undefined, owner)).data.changes.filter((c: any) => c.type === "parcel").map((c: any) => c.parcel).find((p: any) => p.id === "p1");
    expect(p1.photoId).toBe("legacy_101");
  });

  it("Cron xóa ảnh quá 90 ngày và ghi nhật ký; ảnh mới giữ nguyên", async () => {
    await importParcels([parcel("p1")]);
    await scan(phoneA, "TRK-p1");
    expect((await put(phoneA, "photo_old1", "parcelId=p1")).status).toBe(200);
    const s = await stub();
    await runInDurableObject(s, (_i: ShopDO, state: DurableObjectState) => { state.storage.sql.exec("UPDATE photos SET taken_at = 1 WHERE photo_id = 'photo_old1'"); });
    const out = await runInDurableObject(s, (inst: ShopDO) => inst.cleanup());
    expect(out.photosExpired).toBe(1);
    const gone = await SELF.fetch(`${BASE}/photos/photo_old1`, { headers: { Authorization: `Bearer ${phoneA}` } });
    expect(gone.status).toBe(410);
    expect(await E.IMAGES.get("p/photo_old1")).toBeNull();
    const out2 = await runInDurableObject(s, (inst: ShopDO) => inst.cleanup());
    expect(out2.photosExpired).toBe(0);
  });
});

describe("cổng AI (khóa chỉ nằm trên cloud)", () => {
  const realFetch = globalThis.fetch;
  let seen: { url: string; auth: string | null; body: string }[] = [];
  const stubUpstream = (status = 200, body: unknown = { choices: [{ message: { role: "assistant", content: "xin chào" } }] }, headers: Record<string, string> = {}) => {
    seen = [];
    vi.stubGlobal("fetch", async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
      if (!url.startsWith("https://api.groq.com") && !url.startsWith("https://generativelanguage.googleapis.com") && !url.startsWith("https://integrate.api.nvidia.com") && !url.startsWith("https://api.cloudflare.com")) return realFetch(input, init);
      seen.push({ url, auth: new Headers(init?.headers).get("Authorization"), body: new TextDecoder().decode(init?.body as ArrayBuffer) });
      return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });
    });
  };
  afterEach(() => vi.unstubAllGlobals());

  const ask = (token: string | undefined, provider = "groq", body: unknown = { model: "m", messages: [{ role: "user", content: "hi" }] }) =>
    SELF.fetch(`${BASE}/ai/${provider}/chat/completions`, {
      method: "POST", headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: JSON.stringify(body),
    });

  it("chuyển tiếp tới Groq bằng khóa của cloud; khóa không bao giờ lộ cho máy khách", async () => {
    stubUpstream();
    const res = await ask(phoneA);
    expect(res.status).toBe(200);
    const text = await res.text();
    expect(text).toContain("xin chào");
    expect(text).not.toContain("test-groq-key");
    expect(seen).toHaveLength(1);
    expect(seen[0]!.url).toBe("https://api.groq.com/openai/v1/chat/completions");
    expect(seen[0]!.auth).toBe("Bearer test-groq-key");     // khóa của cloud, KHÔNG phải token thiết bị
    expect(seen[0]!.auth).not.toContain(phoneA);
    expect(JSON.parse(seen[0]!.body).messages[0].content).toBe("hi");
  });

  it("NVIDIA NIM (tuyến dự phòng cuối): chuyển tiếp tới integrate.api.nvidia.com bằng khóa NVIDIA của cloud", async () => {
    stubUpstream();
    const res = await ask(phoneA, "nvidia");
    expect(res.status).toBe(200);
    expect(seen[0]!.url).toBe("https://integrate.api.nvidia.com/v1/chat/completions");
    expect(seen[0]!.auth).toBe("Bearer test-nvidia-key");
    expect(await res.text()).not.toContain("test-nvidia-key");
    expect((await ask(phoneA, "openai")).status).toBe(404);   // nhà cung cấp lạ: không chuyển tiếp
  });

  it("Cloudflare Workers AI: không có binding AI (máy thử) thì báo chưa cấu hình, không gọi mạng", async () => {
    stubUpstream();
    const res = await ask(phoneA, "cloudflare");
    expect(res.status).toBe(503);
    expect(seen).toHaveLength(0);
  });

  it("cần token thiết bị; chưa cấu hình khóa nhà cung cấp thì báo rõ", async () => {
    stubUpstream();
    expect((await ask(undefined)).status).toBe(401);
    const noKey = await ask(phoneA, "gemini");
    expect(noKey.status).toBe(503);
    expect((await noKey.json() as any).error.code).toBe("ai_not_configured");
    expect(seen).toHaveLength(0);
  });

  it("giữ nguyên mã lỗi và Retry-After của nhà cung cấp để máy khách tự chuyển mô hình/chờ", async () => {
    stubUpstream(429, { error: { message: "Rate limit. Please try again in 659ms." } }, { "Retry-After": "1" });
    const res = await ask(phoneA);
    expect(res.status).toBe(429);
    expect(res.headers.get("Retry-After")).toBe("1");
    expect(JSON.stringify(await res.json())).toContain("659ms");
  });

  it("giới hạn số lượt hỏi AI mỗi ngày cho cả cửa hàng (5 trong thử nghiệm), rồi trả 429", async () => {
    stubUpstream();
    for (let i = 0; i < 5; i++) expect((await ask(phoneA)).status).toBe(200);
    const over = await ask(phoneB);
    expect(over.status).toBe(429);
    expect((await over.json() as any).error.code).toBe("ai_quota");
    const st = (await call("GET", "/ai/status", undefined, owner)).data;
    expect(st).toMatchObject({ groq: true, gemini: false, nvidia: true, cloudflare: false, usedToday: 5, dailyLimit: 5 });
  });

  it("từ chối nội dung rỗng", async () => {
    stubUpstream();
    const res = await SELF.fetch(`${BASE}/ai/groq/chat/completions`, { method: "POST", headers: { Authorization: `Bearer ${phoneA}` }, body: "" });
    expect(res.status).toBe(422);
  });
});

describe("hết lượt ghi của Cloudflare (gói miễn phí)", () => {
  it("lỗi chặn ghi thành 429 'hết hạn mức', đọc vẫn được, hạn mức báo stop; ngày sau hết chặn", async () => {
    await importParcels([parcel("p1")]);
    const s: any = await stub();   // any: tránh lỗi "Type instantiation is excessively deep" của kiểu stub
    await (runInDurableObject as any)(s, (inst: any) => {
      const db = inst.db;
      inst.__origRun = db.run.bind(db);
      db.run = (q: string, ...b: unknown[]) => {
        if (/^\s*(INSERT|UPDATE|DELETE)/i.test(q)) throw new Error("Exceeded allowed rows written in Durable Objects free tier.");
        return inst.__origRun(q, ...b);
      };
    });
    const w = await call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "editNote", id: "p1", userNote: "x" }] }, phoneA);
    expect(w.status).toBe(429);
    expect(w.data.error.code).toBe("quota_exceeded");
    // máy lâu không thấy (sẽ cần ghi "lần cuối thấy máy") vẫn đọc được
    await (runInDurableObject as any)(s, (inst: any) => { inst.__origRun("UPDATE devices SET last_seen_at = 0"); });
    const r = await call("GET", "/sync?since=0", undefined, phoneA);
    expect(r.status).toBe(200);
    expect((await call("GET", "/me", undefined, phoneB)).status).toBe(200);
    expect(r.data.changes.some((c: any) => c.type === "parcel" && c.parcel.id === "p1")).toBe(true);
    const q = await call("GET", "/quota", undefined, owner);
    expect(q.status).toBe(200);
    expect(q.data.level).toBe("stop");
    await (runInDurableObject as any)(s, (inst: any) => { inst.db.run = inst.__origRun; inst.hardStopDay = "2000-01-01"; });
    expect((await call("GET", "/quota", undefined, owner)).data.level).not.toBe("stop");
  });
});

describe("hạn mức và thời gian thực", () => {
  it("đếm yêu cầu và hàng ghi trong ngày, mức 'ok' khi dùng ít", async () => {
    await importParcels([parcel("p1"), parcel("p2")]);
    const q = (await call("GET", "/quota", undefined, owner)).data;
    expect(q.requests.used).toBeGreaterThan(5);
    expect(q.rowsWritten.used).toBeGreaterThan(0);
    expect(q.requests.limit).toBe(100000);
    expect(q.level).toBe("ok");
  });

  it("khi gần hết hạn mức: chặn thao tác ghi thường nhưng VẪN cho quét", async () => {
    await importParcels([parcel("p1"), parcel("p2")]);
    const s = await stub();
    await runInDurableObject(s, (_i: ShopDO, state: DurableObjectState) => {
      const day = new Date().toISOString().slice(0, 10);
      state.storage.sql.exec("INSERT OR REPLACE INTO quota(day, requests, rows_written, rows_read) VALUES (?, 0, 80000, 0)", day);
    });
    const edit = await call("POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "editNote", id: "p1", userNote: "x" }] }, owner);
    expect(edit.data.results[0]).toMatchObject({ ok: false, error: { code: "quota_exceeded" } });
    const sc = await scan(phoneA, "TRK-p1");
    expect(sc.res.result.outcome).toBe("received");
    const imp = await importParcels([parcel("p3")]);
    expect(imp.status).toBe(429);
    // 91%: dừng cả quét để không bao giờ chạm trần thật (Cloudflare khóa cả đọc tới 07:00)
    await runInDurableObject(s, (_i: ShopDO, state: DurableObjectState) => {
      const day = new Date().toISOString().slice(0, 10);
      state.storage.sql.exec("UPDATE quota SET rows_written = 91000 WHERE day = ?", day);
    });
    const sc2 = await scan(phoneA, "TRK-p2");
    expect(sc2.res).toMatchObject({ ok: false, error: { code: "quota_exceeded" } });
  });

  it("ngưỡng: dưới 50% ok, từ 50% cảnh báo", async () => {
    const s = await stub();
    const set = (n: number) => runInDurableObject(s, (_i: ShopDO, state: DurableObjectState) => {
      state.storage.sql.exec("INSERT OR REPLACE INTO quota(day, requests, rows_written, rows_read) VALUES (?, 0, ?, 0)", new Date().toISOString().slice(0, 10), n);
    });
    await set(49000);
    expect((await call("GET", "/quota", undefined, owner)).data.level).toBe("ok");
    await set(51000);
    expect((await call("GET", "/quota", undefined, owner)).data.level).toBe("warn");
    await set(76000);
    expect((await call("GET", "/quota", undefined, owner)).data).toMatchObject({ level: "stop", scanStop: false });
  });

  it("WebSocket: nhận rev khi kết nối và khi có thay đổi", async () => {
    await importParcels([parcel("p1")]);
    const res = await SELF.fetch(`${BASE}/ws?token=${phoneB}`, { headers: { Upgrade: "websocket" } });
    expect(res.status).toBe(101);
    const ws = res.webSocket!;
    ws.accept();
    const msgs: any[] = [];
    ws.addEventListener("message", (e: MessageEvent) => msgs.push(JSON.parse(e.data as string)));
    await scan(phoneA, "TRK-p1");
    for (let i = 0; i < 50 && msgs.length < 2; i++) await new Promise((r) => setTimeout(r, 20));
    expect(msgs[0].t).toBe("rev");
    expect(msgs.length).toBeGreaterThanOrEqual(2);
    expect(msgs[msgs.length - 1].rev).toBeGreaterThan(msgs[0].rev);
    ws.close();
  });

  it("WebSocket với token sai thì bị từ chối", async () => {
    const res = await SELF.fetch(`${BASE}/ws?token=sai`, { headers: { Upgrade: "websocket" } });
    expect(res.status).toBe(401);
  });
});
