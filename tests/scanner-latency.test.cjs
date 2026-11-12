const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(process.env.SCANNER_LATENCY_SOURCE || path.join(__dirname, '../mobile_scanner.html'), 'utf8');
function fn(name) {
  const match = source.match(new RegExp('^    (?:async )?function ' + name + '\\([^]*?^    }', 'm'));
  assert.ok(match, name);
  return match[0];
}
async function flush() { for (let i = 0; i < 40; i++) await Promise.resolve(); }
function setup(mode, options = {}) {
  const accepted = [], timers = [], frames = [];
  let nativeResolve, nativeCalls = 0, barcodeCalls = 0, qrCalls = 0;
  const nativeResult = new Promise(resolve => { nativeResolve = resolve; });
  const canvas = () => ({width: 0, height: 0, toDataURL: () => 'data:image/jpeg;base64,frame', getContext: () => ({drawImage() {}, getImageData: () => ({data: new Uint8ClampedArray(4)})})});
  const context = vm.createContext({
    console, Promise, Uint8ClampedArray,
    isCameraScanning: false, scannerGeneration: 1, scannerDecodeRevision: 0,
    scannerDecodeMode: mode, mediaStream: {}, stillDecodeBusy: false,
    quaggaDecodeBusy: false, nativeReaders: {}, nativeReadsBusy: {}, scanCanvas: null, stripCanvas: null,
    setTimeout: (callback, ms) => { const timer = {callback, ms}; timers.push(timer); return timer; },
    clearTimeout() {}, document: {createElement: canvas}, window: {BarcodeDetector: true},
    BarcodeDetector: class { detect() { nativeCalls++; return nativeResult; } },
    extractCleanTrackingCode: value => String(value || '').trim(),
    handleCameraFrameDecoded: code => accepted.push(code),
    jsQR: () => { qrCalls++; return options.qrCode ? {data: options.qrCode} : null; },
    Quagga: {decodeSingle: (_config, callback) => { barcodeCalls++; callback(options.barcodeCode ? {codeResult: {code: options.barcodeCode}} : null); }},
    makeBarcodeStrip: input => input
  });
  for (const name of ['withScannerTimeout', 'decodeNativeCanvas', 'readQrFrame', 'decodeBarcodeCanvas', 'startSuperScannerLoop']) vm.runInContext(fn(name), context);
  const video = {paused: false, readyState: 2, videoWidth: 1920, videoHeight: 1080,
    requestVideoFrameCallback: callback => frames.push(callback)};
  context.startSuperScannerLoop(video);
  return {context, accepted, timers, frames, nativeResolve, get nativeCalls() {return nativeCalls;}, get barcodeCalls() {return barcodeCalls;}, get qrCalls() {return qrCalls;}};
}
for (const mode of ['barcode', 'auto']) {
  test(mode + ': software barcode is accepted on first frame while native detector is pending', async () => {
    const h = setup(mode, {barcodeCode: 'SPX0123456789'});
    await flush();
    assert.deepEqual(h.accepted, ['SPX0123456789']);
    assert.equal(h.nativeCalls, 1);
    assert.ok(h.frames.length, 'camera must schedule next frame without waiting for native timeout');
  });
}
test('QR fallback is accepted on first frame while native detector is pending', async () => {
  const h = setup('qr', {qrCode: 'QR0123456789'});
  await flush();
  assert.deepEqual(h.accepted, ['QR0123456789']);
  assert.equal(h.barcodeCalls, 0);
});
test('empty QR in auto mode does not wait for a second native QR detector before barcode', async () => {
  const h = setup('auto', {barcodeCode: 'EXACT123456'});
  await flush();
  assert.deepEqual(h.accepted, ['EXACT123456']);
  assert.equal(h.nativeCalls, 1);
});
test('pending native detector stays single flight across camera frames', async () => {
  const h = setup('barcode');
  await flush();
  for (let i = 0; i < 6; i++) { const frame = h.frames.shift(); assert.ok(frame); frame(); await flush(); }
  assert.equal(h.nativeCalls, 1);
  assert.ok(h.barcodeCalls >= 2, 'software barcode keeps receiving frames while native is stuck');
  assert.deepEqual(h.accepted, []);
});
for (const change of ['scannerGeneration++', 'scannerDecodeRevision++', 'isCameraScanning = false', 'stillDecodeBusy = true']) {
  test('late native result ignored after ' + change, async () => {
    const h = setup('barcode');
    await flush();
    vm.runInContext(change, h.context);
    h.nativeResolve([{format: 'code_128', rawValue: 'STALE123456'}]);
    await flush();
    assert.deepEqual(h.accepted, []);
  });
}
test('native result preserves the complete code when software finds nothing', async () => {
  const h = setup('barcode');
  await flush();
  h.nativeResolve([{format: 'code_128', rawValue: 'SPX0000123456789'}]);
  await flush();
  assert.deepEqual(h.accepted, ['SPX0000123456789']);
});
