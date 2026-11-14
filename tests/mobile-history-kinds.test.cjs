const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const mobile = fs.readFileSync(path.join(__dirname, '..', 'mobile_scanner.html'), 'utf8');
const pc = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function fn(name) {
  const m = mobile.match(new RegExp('^    function ' + name + '\\([\\s\\S]*?^    }', 'm'));
  assert.ok(m, 'thiếu hàm ' + name); return m[0];
}
function ctxWith(mode) {
  const c = vm.createContext({ currentMobileMode: mode });
  vm.runInContext(fn('historyEntryKind') + '\n' + fn('currentHistoryKind'), c);
  return c;
}

test('phân loại lịch sử: hoàn kho / đơn mới / đơn hủy', () => {
  const c = ctxWith('return');
  assert.strictEqual(c.historyEntryKind({ code: 'A' }), 'return');
  assert.strictEqual(c.historyEntryKind({ code: 'A', created: true }), 'new');
  assert.strictEqual(c.historyEntryKind({ code: 'A', detail: '✨ Đơn mới quét gửi PC' }), 'new');
  assert.strictEqual(c.historyEntryKind({ code: 'A', cancelled: true, created: false }), 'cancel');
  assert.strictEqual(c.currentHistoryKind(), 'return');
  assert.strictEqual(ctxWith('new_order').currentHistoryKind(), 'new');
  assert.strictEqual(ctxWith('cancel_order').currentHistoryKind(), 'cancel');
});

test('chỉ còn các nút lọc đúng yêu cầu; đơn hủy ẩn nút "Không có"', () => {
  assert.ok(!/id="filterAll"/.test(mobile));
  for (const id of ['filterOk', 'filterDup', 'filterNotFound', 'filterUnsynced']) assert.ok(new RegExp('id="' + id + '"').test(mobile), id);
  assert.ok(/viewKind === 'cancel' \? 'none' : ''/.test(mobile));
});

test('xóa lịch sử và xóa từng mục chỉ trong đúng loại; có nút đẩy ra bảng chính ở mục đơn mới', () => {
  assert.ok(/historyEntryKind\(e\) === kind/.test(mobile));
  assert.ok(/viewKind === 'new' && !entry\.pushed/.test(mobile));
  assert.ok(/type: 'inbox_push'/.test(mobile));
  assert.ok(/data\.type === 'inbox_push'/.test(pc) && /importSingleNewOrder\(inboxItem\.id\)/.test(pc));
});

test('chờ đồng bộ: hàng đợi giữ cờ cancelled khi gửi lại sang máy tính; PC xử lý cả hai kiểu', () => {
  assert.ok(/sendPayloadToPc\(\{ \.\.\.\(item\.payload \|\| \{\}\), type: item\.kind === 'new_order_scan' \? 'new_order_scan' : 'sync_item'/.test(mobile));
  assert.ok(/data\.type === 'new_order_scan' && data\.cancelled === true\) return handleCancelledOrderScan/.test(pc));
  assert.ok(/'new_order_scan'\]\.includes\(data\.type\)/.test(pc), 'PC đang nhập file vẫn báo thử lại cho đơn mới');
});

test('camera có nút dừng/tiếp tục và tắt cơ chế tự bật lại khi dừng', () => {
  assert.ok(/id="cameraPauseBtn"/.test(mobile));
  const f = mobile.match(/^    async function toggleCameraPause\([\s\S]*?^    }/m)[0];
  assert.ok(/wasCameraActive = false/.test(f) && /safelyStopAndClearScanner\(\)/.test(f) && /startCameraScanner\(false\)/.test(f));
});

test('renderScanHistory chạy thật: mỗi mục chỉ hiện đúng loại, đếm đúng, nút đẩy chỉ ở mục đơn mới', () => {
  const mk = () => ({ children: [], style: {}, dataset: {}, classList: { add() {}, remove() {} }, set innerHTML(v) { this.children = []; }, get innerHTML() { return ''; }, appendChild(c) { this.children.push(c); return c; } });
  const els = {};
  ['mobileHistoryList', 'histCountTotal', 'histCountOk', 'histCountDup', 'histCountNotFound', 'histCountUnsynced', 'historyTitleText', 'filterNotFound'].forEach(id => { els[id] = mk(); });
  const c = vm.createContext({
    currentMobileMode: 'return', currentHistoryFilter: 'all', els,
    document: { getElementById: id => els[id] || null, createElement: mk },
    scanHistory: [
      { code: 'R1', time: 1, status: 'success', synced: true, scanConfirmed: true },
      { code: 'N1', time: 2, status: 'new_order', synced: true, scanConfirmed: true, created: true },
      { code: 'N2', time: 3, status: 'duplicate', created: true },
      { code: 'C1', time: 4, status: 'new_order', synced: true, scanConfirmed: true, cancelled: true },
      { code: 'C2', time: 5, status: 'pending', cancelled: true, synced: false },
    ],
    deleteSingleHistoryItem() {}, pushNewOrderToMain() {}, Date, console,
  });
  vm.runInContext(fn('historyEntryKind') + fn('currentHistoryKind') + fn('renderScanHistory'), c);
  const count = () => c.els.mobileHistoryList.children.length;
  const text = id => c.els[id].textContent;
  vm.runInContext('renderScanHistory()', c);
  assert.strictEqual(count(), 1); assert.strictEqual(text('histCountTotal'), 1);
  vm.runInContext("currentMobileMode = 'new_order'; renderScanHistory()", c);
  assert.strictEqual(count(), 2); assert.strictEqual(text('histCountOk'), 1); assert.strictEqual(text('histCountDup'), 1);
  const buttons = c.els.mobileHistoryList.children.flatMap(row => row.children[0].children).filter(x => /Đẩy ra bảng chính/.test(x.textContent || ''));
  assert.strictEqual(buttons.length, 1, 'chỉ đơn mới đã đồng bộ mới có nút đẩy');
  vm.runInContext("currentMobileMode = 'cancel_order'; renderScanHistory()", c);
  assert.strictEqual(count(), 2); assert.strictEqual(text('histCountUnsynced'), 1);
  assert.strictEqual(c.els.filterNotFound.style.display, 'none');
  assert.match(text('historyTitleText'), /ĐƠN HỦY/);
});
