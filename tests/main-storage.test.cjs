const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const { EventEmitter } = require('node:events');

function harness(t, overrides = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'main-storage-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const handlers = new Map();
  const app = new EventEmitter();
  Object.assign(app, { requestSingleInstanceLock: () => true, getPath: () => dir,
    whenReady: () => new Promise(() => {}), quitCalls: 0,
    quit() { this.quitCalls++; this.emit('before-quit', { preventDefault() {} }); } });
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../main.js'), 'utf8'), {
    require(name) {
      if (name === 'electron') return { app, ipcMain: { handle: (key, fn) => handlers.set(key, fn) } };
      if (name === 'fs') return { ...fs, promises: { ...fs.promises, ...overrides } };
      return require(name);
    }, __dirname: path.join(__dirname, '..'), process: { argv: [] }, console, Buffer
  });
  return { app, save: data => handlers.get('save-hard-drive-data')({}, data),
    file: path.join(dir, 'QuanLyHangHoan/du_lieu_hang_hoan.json') };
}
function gate() { let release; const promise = new Promise(resolve => { release = resolve; }); return { promise, release }; }

test('receipt waits for fsync and atomic rename', async t => {
  const blocked = gate();
  const h = harness(t, { rename: async (...args) => { await blocked.promise; return fs.promises.rename(...args); } });
  let settled = false;
  const receipt = h.save([{ id: 1 }]).then(value => { settled = true; return value; });
  await new Promise(resolve => setTimeout(resolve, 25));
  assert.equal(settled, false);
  blocked.release();
  assert.equal((await receipt).success, true);
  assert.deepEqual(JSON.parse(fs.readFileSync(h.file)), [{ id: 1 }]);
});
test('ENOSPC returns false and preserves the existing file', async t => {
  const h = harness(t, { open: async () => { throw Object.assign(new Error('disk full'), { code: 'ENOSPC' }); } });
  fs.mkdirSync(path.dirname(h.file), { recursive: true });
  fs.writeFileSync(h.file, '[{"id":"old"}]');
  assert.equal((await h.save([{ id: 'new' }])).success, false);
  assert.equal(fs.readFileSync(h.file, 'utf8'), '[{"id":"old"}]');
});
test('rename failure never falls back to overwriting live data', async t => {
  const h = harness(t, { rename: async () => { throw new Error('rename denied'); } });
  fs.mkdirSync(path.dirname(h.file), { recursive: true }); fs.writeFileSync(h.file, '[]');
  assert.equal((await h.save([{ id: 'new' }])).success, false);
  assert.equal(fs.readFileSync(h.file, 'utf8'), '[]');
  assert.deepEqual(fs.readdirSync(path.dirname(h.file)), [path.basename(h.file)]);
});
test('concurrent receipts commit in order and snapshot input at invocation', async t => {
  const h = harness(t); const first = [{ id: 1 }];
  const a = h.save(first); first[0].id = 'mutated';
  const b = h.save([{ id: 2 }]); const c = h.save([{ id: 3 }]);
  assert.deepEqual((await Promise.all([a, b, c])).map(x => x.success), [true, true, true]);
  assert.deepEqual(JSON.parse(fs.readFileSync(h.file)), [{ id: 3 }]);
});
test('quit prevents exit until all queued saves settle', async t => {
  const blocked = gate();
  const h = harness(t, { rename: async (...args) => { await blocked.promise; return fs.promises.rename(...args); } });
  const a = h.save([{ id: 1 }]); const b = h.save([{ id: 2 }]);
  let prevented = false; h.app.emit('before-quit', { preventDefault() { prevented = true; } });
  assert.equal(prevented, true); assert.equal(h.app.quitCalls, 0);
  blocked.release(); await Promise.all([a, b]);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(h.app.quitCalls, 1);
  assert.deepEqual(JSON.parse(fs.readFileSync(h.file)), [{ id: 2 }]);
});
test('invalid data is rejected before touching disk', async t => {
  const h = harness(t); assert.equal((await h.save({ orders: [] })).success, false);
  assert.equal(fs.existsSync(h.file), false);
});
test('fsync failure returns a failed receipt, preserves data and later saves recover', async t => {
  let failSync = true;
  const h = harness(t, { open: async (...args) => {
    const handle = await fs.promises.open(...args);
    return { writeFile: (...values) => handle.writeFile(...values), close: () => handle.close(),
      sync: async () => { if (failSync) throw new Error('fsync failed'); await handle.sync(); } };
  } });
  fs.mkdirSync(path.dirname(h.file), { recursive: true }); fs.writeFileSync(h.file, '[]');
  const failed = await h.save([{ id: 1 }]);
  assert.equal(failed.success, false); assert.match(failed.error, /fsync failed/);
  assert.equal(fs.readFileSync(h.file, 'utf8'), '[]');
  failSync = false;
  assert.equal((await h.save([{ id: 2 }])).success, true);
  assert.deepEqual(JSON.parse(fs.readFileSync(h.file)), [{ id: 2 }]);
});
test('quit drains failed writes and saves submitted during the drain', async t => {
  const blocked = gate(); let writes = 0;
  const h = harness(t, { rename: async (...args) => {
    writes++; if (writes === 1) { await blocked.promise; throw new Error('rename failed'); }
    return fs.promises.rename(...args);
  } });
  const first = h.save([{ id: 1 }]);
  h.app.emit('before-quit', { preventDefault() {} });
  const last = h.save([{ id: 2 }]);
  blocked.release();
  assert.equal((await first).success, false); assert.equal((await last).success, true);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(h.app.quitCalls, 1);
  assert.deepEqual(JSON.parse(fs.readFileSync(h.file)), [{ id: 2 }]);
  assert.equal((await h.save([])).success, false);
});
