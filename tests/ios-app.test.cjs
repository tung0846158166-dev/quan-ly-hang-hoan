const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), path = require('path');
const root = path.join(__dirname, '..');
test('khung app iPhone: cấu hình Capacitor, workflow build không ký, quyền camera', () => {
  const cfg = JSON.parse(fs.readFileSync(path.join(root, 'ios-app/capacitor.config.json'), 'utf8'));
  assert.strictEqual(cfg.webDir, 'www');
  const pkg = JSON.parse(fs.readFileSync(path.join(root, 'ios-app/package.json'), 'utf8'));
  assert.ok(pkg.dependencies['@capacitor/ios'] && pkg.devDependencies['@capacitor/cli']);
  const wf = fs.readFileSync(path.join(root, '.github/workflows/build-ios-app.yml'), 'utf8');
  assert.match(wf, /cp \.\.\/mobile_scanner\.html www\/index\.html/);
  assert.match(wf, /NSCameraUsageDescription/);
  assert.match(wf, /CODE_SIGNING_ALLOWED=NO/);
  assert.match(wf, /VERSION/);
  assert.match(wf, /make_latest: "false"/);
});
test('điện thoại nhớ mã phòng giữa các lần mở app', () => {
  const html = fs.readFileSync(path.join(root, 'mobile_scanner.html'), 'utf8');
  assert.match(html, /localStorage\.getItem\('scanner-last-room'\)/);
  assert.match(html, /localStorage\.setItem\('scanner-last-room', room\)/);
});
