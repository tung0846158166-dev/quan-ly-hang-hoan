const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const { EventEmitter } = require('node:events');

const html = fs.readFileSync(path.join(__dirname, '../Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function extract(name) {
  const start = html.search(new RegExp('    (?:async )?function ' + name + '\\('));
  assert(start >= 0, name);
  const rest = html.slice(start);
  return rest.slice(0, rest.indexOf('\n    }') + 6);
}
const PHOTO = 'data:image/jpeg;base64,' + 'A'.repeat(400);
function persistContext(api, extra = {}) {
  const c = vm.createContext({
    orders: [], photoMemoryCache: new Map(), STORAGE_KEY: 'main', diskPersistDebounceTimer: null, console, clearTimeout, setTimeout,
    localStorage: { setItem() {} }, savePhotoToDb: async () => true, saveOrdersToSafeStore: async () => true,
    updateSafeStorageHeaderBadge() {}, updateDiskNoticeStatus() {}, showAppToast() {}, window: { desktopDiskAPI: api }, ...extra
  });
  vm.runInContext(extract('flushPersistOrdersToDiskAndStorage'), c);
  return c;
}

test('concurrent saves are grouped and each receipt covers its own change', async () => {
  const writes = [];
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  const c = persistContext({ saveToDisk: async data => { writes.push(data.map(o => o.id)); if (writes.length === 1) await gate; return { success: true }; } });
  c.orders = [{ id: 1 }];
  const first = c.flushPersistOrdersToDiskAndStorage();
  await new Promise(r => setTimeout(r, 5));
  c.orders.push({ id: 2 });
  const second = c.flushPersistOrdersToDiskAndStorage();
  c.orders.push({ id: 3 });
  const third = c.flushPersistOrdersToDiskAndStorage();
  release();
  const results = await Promise.all([first, second, third]);
  assert.deepEqual(results.map(r => r.success), [true, true, true]);
  assert.equal(writes.length, 2, 'three requests -> one running write + one shared follow-up');
  assert.deepEqual(writes[1], [1, 2, 3], 'follow-up includes changes made while the first write ran');
});

test('photos are written once as files and kept out of the order list', async () => {
  const photoWrites = [], diskWrites = [], idbWrites = [];
  const c = persistContext({
    savePhotoFiles: async list => { photoWrites.push(list.map(p => p.id)); return { success: true, saved: list.map(p => p.id) }; },
    saveToDisk: async data => { diskWrites.push(data); return { success: true }; }
  }, { saveOrdersToSafeStore: async data => { idbWrites.push(data); return true; } });
  c.orders = [{ id: 1, photo: PHOTO, photoId: 'p1' }, { id: 2 }];
  assert.equal((await c.flushPersistOrdersToDiskAndStorage()).success, true);
  assert.deepEqual(JSON.parse(JSON.stringify(photoWrites)), [['p1']]);
  assert.equal(diskWrites[0][0].photo, '', 'main file no longer carries the photo');
  assert.equal(diskWrites[0][0].photoId, 'p1');
  assert.equal(idbWrites[0][0].photo, '');
  assert.equal(c.orders[0].photo, PHOTO, 'in-memory order keeps its photo');
  await c.flushPersistOrdersToDiskAndStorage();
  assert.equal(photoWrites.length, 1, 'unchanged photo is not rewritten');
  c.orders[0].photo = PHOTO + 'B';
  await c.flushPersistOrdersToDiskAndStorage();
  assert.equal(photoWrites.length, 2, 'a changed photo is rewritten');
});

test('if the photo file cannot be written the photo stays inline so nothing is lost', async () => {
  const diskWrites = [];
  const c = persistContext({
    savePhotoFiles: async () => ({ success: false, saved: [] }),
    saveToDisk: async data => { diskWrites.push(data); return { success: true }; }
  });
  c.orders = [{ id: 1, photo: PHOTO, photoId: 'p1' }];
  assert.equal((await c.flushPersistOrdersToDiskAndStorage()).success, true);
  assert.equal(diskWrites[0][0].photo, PHOTO);
});

test('photos from before this session are moved to files in the background, without blocking the save', async () => {
  const photoWrites = [];
  const c = persistContext({
    savePhotoFiles: async list => { photoWrites.push(list.length); return { success: true, saved: list.map(p => p.id) }; },
    saveToDisk: async () => ({ success: true })
  });
  c.orders = Array.from({ length: 20 }, (_, i) => ({ id: i, photo: PHOTO + i, photoId: 'old' + i }));
  c.flushPersistOrdersToDiskAndStorage.legacyPhotoIds = new Set(c.orders.map(o => o.photoId));
  await c.flushPersistOrdersToDiskAndStorage();
  assert.deepEqual(photoWrites, [], 'legacy photos are not written on the scan path');
  assert.equal(c.flushPersistOrdersToDiskAndStorage.state.migrating, true);
});

test('tracking codes are cleaned once per order until its codes change', () => {
  let calls = 0;
  const c = vm.createContext({ extractCleanTrackingCode: raw => { calls++; return String(raw).toUpperCase(); } });
  vm.runInContext(extract('getOrderTrackingCodes'), c);
  const order = { track: 'a1', altTrack: 'b2' };
  assert.deepEqual([...c.getOrderTrackingCodes(order)], ['A1', 'B2']);
  const afterFirst = calls;
  c.getOrderTrackingCodes(order).push('MUTATED');
  assert.deepEqual([...c.getOrderTrackingCodes(order)], ['A1', 'B2'], 'callers cannot corrupt the cache');
  assert.equal(calls, afterFirst, 'second lookup reuses the cache');
  order.track = 'c3';
  assert.deepEqual([...c.getOrderTrackingCodes(order)], ['C3', 'B2']);
});

function mainHarness(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'photo-files-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const handlers = new Map();
  const app = new EventEmitter();
  Object.assign(app, { requestSingleInstanceLock: () => true, getPath: () => dir, whenReady: () => new Promise(() => {}), quit() {} });
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../main.js'), 'utf8'), {
    require(name) { return name === 'electron' ? { app, ipcMain: { handle: (k, fn) => handlers.set(k, fn) } } : require(name); },
    __dirname: path.join(__dirname, '..'), process: { argv: [] }, console, Buffer
  });
  return { call: (k, ...a) => handlers.get(k)({}, ...a), dir: path.join(dir, 'QuanLyHangHoan') };
}

test('main process stores photos as separate files, skips identical rewrites and reads them back', async t => {
  const h = mainHarness(t);
  const saved = await h.call('save-photo-files', [{ id: 'p1', data: PHOTO }, { id: 'p2', data: PHOTO + 'x' }]);
  assert.equal(saved.success, true);
  assert.deepEqual([...saved.saved].sort(), ['p1', 'p2']);
  const files = fs.readdirSync(path.join(h.dir, 'photos'));
  assert.equal(files.length, 2);
  const before = fs.statSync(path.join(h.dir, 'photos', files[0])).mtimeMs;
  await new Promise(r => setTimeout(r, 20));
  assert.equal((await h.call('save-photo-files', [{ id: 'p1', data: PHOTO }, { id: 'p2', data: PHOTO + 'x' }])).success, true);
  assert.equal(fs.statSync(path.join(h.dir, 'photos', files[0])).mtimeMs, before, 'identical photo is not rewritten');
  const read = await h.call('read-photo-files', ['p1', 'p2', 'missing']);
  assert.deepEqual({ ...read.photos }, { p1: PHOTO, p2: PHOTO + 'x' });
  assert.deepEqual(fs.readdirSync(path.join(h.dir, 'photos')).filter(f => f.endsWith('.tmp')), []);
  assert.equal((await h.call('save-photo-files', 'bad')).success, false);
});

const mobileHtml = fs.readFileSync(path.join(__dirname, '../mobile_scanner.html'), 'utf8');
function mobileFn(name) {
  const start = mobileHtml.search(new RegExp('    (?:async )?function ' + name + '\\('));
  assert(start >= 0, name);
  const rest = mobileHtml.slice(start + 4);
  const end = rest.slice(1).search(/\n    (?:async )?function /);
  return end < 0 ? rest : rest.slice(0, end + 1);
}
function pauseContext(queue, alive) {
  const c = vm.createContext({ console, Date, Math, MAX_UNCONFIRMED_SCANS: 5, PC_ALIVE_WINDOW_MS: 45000,
    lastPcReceiptAt: alive ? Date.now() : 0, offlineQueue: queue, extractCleanTrackingCode: x => String(x).toUpperCase() });
  vm.runInContext(['getUnconfirmedScans', 'isPcRecentlyAlive', 'shouldPauseForUnconfirmed'].map(mobileFn).join('\n'), c);
  return c;
}
const pendingItems = n => Array.from({ length: n }, (_, i) => ({ code: 'CODE000' + i, time: Date.now() }));

test('phone pauses new codes only when PC is online but too many scans are unconfirmed', () => {
  assert.equal(pauseContext(pendingItems(4), true).shouldPauseForUnconfirmed('NEWCODE1'), false);
  assert.equal(pauseContext(pendingItems(5), true).shouldPauseForUnconfirmed('NEWCODE1'), true);
  assert.equal(pauseContext(pendingItems(5), true).shouldPauseForUnconfirmed('CODE0003'), false, 'a waiting code can still be retried');
  assert.equal(pauseContext(pendingItems(9), false).shouldPauseForUnconfirmed('NEWCODE1'), false, 'PC offline keeps the offline queue working');
  const confirmed = pendingItems(5).map(item => ({ ...item, scanConfirmed: true }));
  assert.equal(pauseContext(confirmed, true).shouldPauseForUnconfirmed('NEWCODE1'), false);
  const review = pendingItems(5).map(item => ({ ...item, reviewRequired: true }));
  assert.equal(pauseContext(review, true).shouldPauseForUnconfirmed('NEWCODE1'), false);
});
