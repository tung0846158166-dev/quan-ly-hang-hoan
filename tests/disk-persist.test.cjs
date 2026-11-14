const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const vm = require('vm');
const { createAuxStore, AUX_KEYS } = require('../disk_aux');

const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
const tmp = () => fs.mkdtempSync(path.join(os.tmpdir(), 'qlhh-'));

test('ghi/đọc dữ liệu phụ qua file thật, khóa ngoài danh sách bị từ chối, null xóa file', async () => {
  const dir = tmp();
  const store = createAuxStore(() => dir);
  const key = 'shopee_cancelled_orders_v1';
  assert.deepStrictEqual((await store.save(key, '[{"id":1}]')).success, true);
  await store.save('shopee_archived_orders_v1', '[{"id":2}]');
  const r = store.readAllSync();
  assert.strictEqual(r.data[key], '[{"id":1}]');
  assert.strictEqual(r.data['shopee_archived_orders_v1'], '[{"id":2}]');
  assert.strictEqual((await store.save('../evil', 'x')).success, false);
  assert.strictEqual((await store.save(key, 123)).success, false);
  await store.save(key, null);
  assert.ok(!(key in store.readAllSync().data));
  assert.ok(!fs.readdirSync(dir).some(n => n.endsWith('.tmp')), 'không để file tạm');
});

test('ghi liên tiếp giữ đúng thứ tự (giá trị cuối thắng)', async () => {
  const dir = tmp();
  const store = createAuxStore(() => dir);
  const key = 'shopee_new_orders_inbox_v1';
  await Promise.all(Array.from({ length: 20 }, (_, i) => store.save(key, JSON.stringify([i]))));
  assert.strictEqual(store.readAllSync().data[key], '[19]');
});

test('file hỏng không làm hỏng lúc khởi động', async () => {
  const dir = tmp();
  const store = createAuxStore(() => dir);
  fs.writeFileSync(store.fileOf('shopee_archived_orders_v1'), '{hỏng');
  await store.save('shopee_cancelled_orders_v1', '[1]');
  const r = store.readAllSync();
  assert.ok(!('shopee_archived_orders_v1' in r.data));
  assert.strictEqual(r.data['shopee_cancelled_orders_v1'], '[1]');
});

test('sao lưu tự động: chép file dữ liệu + file phụ, ghi đè trong ngày, giữ 7 ngày gần nhất', async () => {
  const dir = tmp();
  const store = createAuxStore(() => dir);
  fs.writeFileSync(path.join(dir, 'du_lieu_hang_hoan.json'), '[{"a":1}]');
  fs.writeFileSync(path.join(dir, 'file_khac.txt'), 'x');
  await store.save('shopee_cancelled_orders_v1', '[1]');
  for (let d = 1; d <= 9; d++) assert.strictEqual(store.runBackup(new Date(2026, 10, d, 9, 0)).success, true);
  const root = path.join(dir, 'sao_luu_tu_dong');
  const days = fs.readdirSync(root).sort();
  assert.strictEqual(days.length, 7);
  assert.strictEqual(days[0], '2026-11-03'); assert.strictEqual(days[6], '2026-11-09');
  const files = fs.readdirSync(path.join(root, '2026-11-09')).sort();
  assert.deepStrictEqual(files, ['du_lieu_hang_hoan.json', 'phu_shopee_cancelled_orders_v1.json']);
  fs.writeFileSync(path.join(dir, 'du_lieu_hang_hoan.json'), '[{"a":2}]');
  store.runBackup(new Date(2026, 10, 9, 20, 0));
  assert.strictEqual(fs.readFileSync(path.join(root, '2026-11-09', 'du_lieu_hang_hoan.json'), 'utf8'), '[{"a":2}]');
});

test('danh sách khóa không chứa mật khẩu/trạng thái khóa; main.js, preload và đóng gói đã nối đúng', () => {
  assert.ok(!AUX_KEYS.some(k => /pwd|locked_v1|processed/.test(k)));
  assert.ok(AUX_KEYS.includes('shopee_archived_orders_v1') && AUX_KEYS.includes('shopee_cancelled_orders_v1') && AUX_KEYS.includes('shopee_new_orders_inbox_v1'));
  const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'package.json'), 'utf8'));
  assert.ok(pkg.build.files.includes('disk_aux.js'), 'disk_aux.js phải có trong bản đóng gói');
  const main = fs.readFileSync(path.join(__dirname, '..', 'main.js'), 'utf8');
  assert.ok(/createAuxStore/.test(main) && /disk_aux/.test(main) && /ipcMain\.on\('aux-read-all-sync'/.test(main) && /ipcMain\.handle\('aux-save'/.test(main) && /runBackup\(\)/.test(main));
  const pre = fs.readFileSync(path.join(__dirname, '..', 'preload.js'), 'utf8');
  assert.ok(/auxReadAllSync/.test(pre) && /auxSave/.test(pre));
});

function runMirror({ localData = {}, diskData = {}, quotaFor = null }) {
  const start = html.indexOf('    (function installDiskMirror() {');
  const end = html.indexOf('    })();', start) + '    })();'.length;
  const code = html.slice(start, end);
  const saves = [];
  const timers = [];
  const store = { ...localData };
  class Storage {}
  Storage.prototype.setItem = function (k, v) { if (quotaFor && quotaFor === k) throw new Error('QuotaExceededError'); store[k] = String(v); };
  Storage.prototype.removeItem = function (k) { delete store[k]; };
  Storage.prototype.getItem = function (k) { return k in store ? store[k] : null; };
  const localStorage = new Storage();
  const window = {
    desktopDiskAPI: {
      auxReadAllSync: () => ({ keys: ['K1', 'K2'], data: diskData }),
      auxSave: (k, v) => { saves.push([k, v]); return Promise.resolve({ success: true }); },
    },
  };
  const ctx = vm.createContext({ window, localStorage, Storage, console, setTimeout: (fn) => { timers.push(fn); return timers.length; }, clearTimeout: () => {} });
  vm.runInContext(code, ctx);
  const runTimers = () => { while (timers.length) timers.shift()(); };
  return { ctx, store, saves, localStorage, window, runTimers };
}

test('mở app: khóa trống trong bộ nhớ được khôi phục từ ổ cứng, khóa có dữ liệu thì giữ nguyên', () => {
  const m = runMirror({ localData: { K2: '[{"live":1}]' }, diskData: { K1: '[{"d":1}]', K2: '[{"old":1}]' } });
  assert.strictEqual(m.store.K1, '[{"d":1}]');
  assert.strictEqual(m.store.K2, '[{"live":1}]');
  const m2 = runMirror({ localData: { K1: '[]' }, diskData: { K1: '[{"d":1}]' } });
  assert.strictEqual(m2.store.K1, '[{"d":1}]', '"[]" coi như trống');
});

test('ghi vào khóa được theo dõi thì ghi ra ổ cứng sau 300ms; khóa khác thì không; xóa thì gửi null', () => {
  const m = runMirror({});
  m.localStorage.setItem('K1', '[1,2]');
  m.localStorage.setItem('OTHER', 'x');
  m.runTimers();
  assert.ok(m.saves.length >= 1 && m.saves.every(s => s[0] === 'K1' && s[1] === '[1,2]'));
  m.saves.length = 0;
  m.localStorage.removeItem('K1');
  m.runTimers();
  assert.ok(m.saves.length >= 1 && m.saves.every(s => s[0] === 'K1' && s[1] === null));
});

test('localStorage đầy (quota) vẫn ghi được giá trị mới ra ổ cứng', () => {
  const m = runMirror({ quotaFor: 'K1' });
  assert.throws(() => m.localStorage.setItem('K1', '[big]'), /Quota/);
  m.runTimers();
  assert.deepStrictEqual(m.saves, [['K1', '[big]']]);
});

test('ảnh đơn hủy ghi ra file trên ổ cứng và lấy lại được khi mất bộ nhớ trình duyệt', () => {
  assert.ok(/api\.savePhotoFiles\(\[\{ id: photoId, data: dataUrl \}\]\)/.test(html));
  assert.ok(/async function restoreCancelledPhotosFromDisk/.test(html) && /api\.readPhotoFiles\(missing\)/.test(html));
});
