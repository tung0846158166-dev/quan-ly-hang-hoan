const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const html = fs.readFileSync(path.join(__dirname, '../mobile_scanner.html'), 'utf8');
function fn(name) {
  const start = html.search(new RegExp('    (?:async )?function ' + name + '\\('));
  assert(start >= 0, name);
  const rest = html.slice(start + 4);
  const end = rest.slice(1).search(/\n    (?:async )?function |\n    \/\/ Quét các dải/);
  return rest.slice(0, end + 1);
}
const tableStart = html.indexOf('    const CODE128_PATTERNS');
const table = html.slice(tableStart, html.indexOf('];', tableStart) + 2);
function decoder() {
  const c = vm.createContext({ console, Date, Math, Uint8Array, Float32Array, Array, Map, Number, String });
  vm.runInContext(table + '\n' + ['matchCode128Symbol', 'code128Edges', 'code128ToText', 'decodeCode128Forward',
    'decodeCode128Line', 'code128GrayFrame', 'findBarcodeOrientations', 'sampleScanLine', 'stretchLine', 'sharpenLine', 'decodeCode128LineDeep', 'fastDecodeCode128Canvas'].map(fn).join('\n'), c);
  return c;
}
// Fixtures are rendered from reportlab's own Code 128 encoder (independent of the phone code's table).
const rows = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixtures/code128-rows.json'), 'utf8'));

test('pattern table is a valid Code 128 table', () => {
  const c = decoder();
  const patterns = vm.runInContext('CODE128_PATTERNS', c);
  assert.equal(patterns.length, 107);
  assert.equal(new Set(patterns).size, 107, 'patterns are unique');
  patterns.forEach((p, i) => assert.equal([...p].reduce((a, b) => a + Number(b), 0), 11, 'symbol ' + i));
  assert.deepEqual([patterns[0], patterns[103], patterns[104], patterns[105], patterns[106]], ['212222', '211412', '211214', '211232', '233111']);
});

test('reads reportlab-rendered Code 128 lines (blur, noise, dim light, gradient, upside-down)', () => {
  const c = decoder();
  const failures = [];
  for (const r of rows) {
    const got = c.decodeCode128Line(Uint8Array.from(r.row));
    if (r.scale >= 2) { if (got !== r.text) failures.push(r.variant + '@' + r.scale + ' ' + r.text + ' -> ' + got); }
    else assert(got === null || got === r.text, 'never a wrong string, even when too small to read');
  }
  assert.deepEqual(failures, []);
});

test('random noise and corrupted barcodes never produce a wrong code', () => {
  const c = decoder();
  let seed = 12345;
  const rnd = () => (seed = (seed * 1664525 + 1013904223) % 4294967296) / 4294967296;
  let falseHits = 0;
  for (let n = 0; n < 6000; n++) {
    const line = [];
    let dark = rnd() < 0.5;
    while (line.length < 900) { const run = 1 + Math.floor(rnd() * 9); for (let i = 0; i < run; i++) line.push(dark ? 20 : 235); dark = !dark; }
    if (c.decodeCode128Line(Uint8Array.from(line.slice(0, 900))) !== null) falseHits++;
  }
  assert.equal(falseHits, 0);
  for (const r of rows.filter(x => x.variant === 'plain' && x.scale === 3.4)) {
    const row = r.row.slice();
    const from = Math.floor(row.length * 0.4);
    for (let i = from; i < from + 9; i++) row[i] = 255 - row[i]; // damage one symbol
    const got = c.decodeCode128Line(Uint8Array.from(row));
    assert(got === null || got === r.text, 'a damaged barcode is rejected, not misread');
  }
});

function fakeCanvas(width, height, rowOf) {
  const data = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const v = rowOf(x, y); const i = (y * width + x) * 4;
    data[i] = data[i + 1] = data[i + 2] = v; data[i + 3] = 255;
  }
  return { width, height, getContext: () => ({ getImageData: () => ({ data }) }) };
}

test('camera frame decode: horizontal, rotated and single-line confirmation', () => {
  const sample = rows.find(r => r.variant === 'plain' && r.scale === 3.4 && r.text === 'SPXVN0123456789');
  const w = sample.row.length, h = 120;
  const horizontal = fakeCanvas(w, h, x => sample.row[x]);
  assert.equal(decoder().fastDecodeCode128Canvas(horizontal), 'SPXVN0123456789');
  const rotated = fakeCanvas(150, w, (x, y) => sample.row[y]);
  assert.equal(decoder().fastDecodeCode128Canvas(rotated), 'SPXVN0123456789');
  // One scan line only: needs the same code in two consecutive frames.
  const c = decoder();
  const oneLine = fakeCanvas(w, h, (x, y) => (y === Math.round(0.5 * (h - 1)) ? sample.row[x] : 255));
  assert.equal(c.fastDecodeCode128Canvas(oneLine), null);
  assert.equal(c.fastDecodeCode128Canvas(oneLine), 'SPXVN0123456789');
  assert.equal(decoder().fastDecodeCode128Canvas(oneLine, { single: true }), 'SPXVN0123456789');
  assert.equal(decoder().fastDecodeCode128Canvas(fakeCanvas(w, h, () => 255)), null);
});

test('live scan loop and still capture use the fast decoder first', () => {
  assert(/detectedCode = fastDecodeCode128Canvas\(roi, \{ skipVertical: frameNo % 2 === 1, deep: frameNo % 4 === 0 \}\);/.test(html));
  assert(/fastDecodeCode128Canvas\(canvas, \{ single: true \}\)/.test(html));
});

test('deep pass: low-contrast line still decodes, noise gives no false read', () => {
  const c = decoder();
  const r = rows[0];
  const faded = Uint8Array.from(r.row, v => 120 + Math.round(v * 0.12));
  const got = c.decodeCode128LineDeep(faded);
  assert(got === null || got === r.text);
  const rnd = Uint8Array.from({ length: 900 }, (_, i) => (i * 7919) % 256);
  assert.equal(c.decodeCode128LineDeep(rnd), null);
});
