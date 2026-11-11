# Plan: fix the 17 confirmed audit findings

User authorization: “Sửa hết những lỗi còn lại đi”. Baseline main: e7ca3b1.
Keep current HTML, Electron application, user data and MQTT/ntfy endpoints. No architectural rewrite or external deployment.

- [x] A01–A03/A11: independent persistence attempts, real disk completion/error receipts, safe archival transaction and compatible backup restore.
- [x] A04/A05/A12/A13/A14: retain pending phone work until matching PC ACK, stable request replay, no premature green, version photo captures.
- [x] A09/A10/A15/A16: missing TikTok status variable, SKU subtotals, size-only variation, cancellation status.
- [x] A06/A07/A08/A17: stable identity for delayed OCR/photo, safe notification/toast rendering, enforce photo deletion locks.
- [x] Run previous notification tests plus new regression tests, browser checks, syntax and Windows workflow lint. Do not label Windows/iPhone/4G tests PASS without actual evidence.
- [x] Independent code review and one covered fix pass; version the release, commit and push feature branch, create PR if API access permits. Do not merge main automatically.

Work partitions use independent patch files for the shared desktop HTML, applied sequentially; agents may edit only their unique test files and owned Main/Preload files. Parent integrates HTML patches and checks the combined behavior.

Contracts: `saveOrders(true)`/`flushPersistOrdersToDiskAndStorage()` provide an awaited persistence result. Callers requiring commit-before-ACK must check failure. Existing debounced calls remain supported and failures must be visible. Main save IPC acknowledges only the caller's completed durable write and rejects failed writes.

Review focus: archive quota rollback, close during disk save, same UUID retries after lost/error ACK, older photo arrival, order insertion/deletion while awaiting OCR/decode, legacy backups and legacy mobile payloads, preservation of original price-column semantics and multi-item rows.

Verification: 56 Node tests pass; 5 Chromium browser checks pass; inline/Main/Preload syntax, actionlint and git diff --check pass. Independent review found five further issues, fixed with regression coverage. Windows/iPhone/4G/real XLSX runtime NOT TESTED. Commit/push and remote Actions status are recorded in the delivery report; GitHub API access may prevent PR creation.
