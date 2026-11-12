# Progress: 2026-11-12-return-parcels-sync.md

Baseline: origin/main 5fc5cd4. Branch fix/return-parcels-sync.
User authorized full batch including S01–S12 and screenshot, with explicit independent return parcels under one original order.

Ruling: preserve one existing table row per independently identifiable return parcel rather than introduce a second application/store — minimizes UI migration and keeps per-parcel statuses/quantities/photos. Original orderId may repeat. Missing parcel metadata is not invented; ambiguous lookup needs review.
Ruling: photo not_found/ambiguous terminal review state is not a success receipt; retain image durably on phone, do not retry forever or attach elsewhere.
Ruling: reject cross-room payloads and remove shared GLOBAL fallback; retain existing public transports. This is room isolation, not a claim of authenticated/private service.

Independent domains use isolated desktop copies and unified patches; mobile edits only its owned file. Import produces shared code resolver for runtime. Parent integrates sequentially and verifies whole application.

Import/PC/mobile integration: complete. npm test 97/97 PASS. Shared helper interfaces verified in source + real browser.
Final review: fresh independent reviewer, 3 Important findings. All fixed with failing tests before changes and full green suite: room-switch replay, inboxphoto crossover/capture mismatch, PCcamera sharedorder firstmatch.
Additional tests: GLOBAL publish removed both endpoints, canonical PCcamera received status.
Browser: four native Chromium checks PASS; no page errors. Syntax/workflow lint/diffcheck PASS. Real Windows/iPhone/4G/XLSX runtime NOT TESTED.
No deferred minor review findings. No main merge.
