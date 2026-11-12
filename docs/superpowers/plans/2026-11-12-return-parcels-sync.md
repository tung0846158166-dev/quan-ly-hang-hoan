# Return parcels and reliable scan sync implementation plan

> **For agentic workers:** Execute the tasks with red/green tests and independent integration review.

**Goal:** Fix S01–S12 audit findings and preserve independently received return parcels under one original order.
**Architecture:** Retain HTML/Electron/JSON architecture. Each return parcel is an existing table record linked by original orderId. Tracking aliases belong to that parcel; shared original identifiers never justify receiving all siblings. Live/offline handlers share a persistence-before-ACK pipeline. Mobile durable queue separates scan confirmation, background photo completion and unmatched review.
**Tech Stack:** Existing JavaScript, Electron, IndexedDB, Node tests; no added dependencies.
**Spec:** User instruction plus /workspace/audit-quan-ly-2026-11-12/scan-import-sync-audit.md. User explicitly authorized all fixes; implementation proceeds without another approval gate.

## Constraints / review focus

- Never erase existing records or reconstruct unknown lost historical tracking from guesses.
- One original order may have A, B and C returned on separate dates with separate tracking; receiving A does not receive B/C.
- Reject ambiguous codes and foreign rooms; ACK success only after persistence.
- Preserve legacy backup/queues and code aliases; retain unresolved photos locally until explicit review.
- Actual Windows/iPhone/4G tests remain NOT TESTED until run on those devices.

## Tasks

- [x] Import: failing regression for separate parcels, repeated imports, code replacement, aliases and shared forward codes; implement getOrderTrackingCodes(order), getPhoneOrderMatches(rawCode), parcel-aware grouping and enrichment.
- [x] PC: failing regression for ambiguous match, foreignroom, stale negative cache and unsafe catchup; unify commit/ACK and matchers, delay heavy visual work, instrument processing durations.
- [x] Mobile: failing regression for premature local outcome, reload ACK, quota and unmatched retry; durable queue with migration, restore pending membership, authoritative outcomes and background photos.
- [x] Integrate isolated desktop patches sequentially, adapt prior tests to intentional contract changes. Run complete npm test, syntax, browser checks, workflow lint and git diff check.
- [x] Independent review of combined branch, fix confirmed findings with regression coverage.
- [ ] Version packaging and artifact, documentation including migration/device limits; commit and push feature branch. Do not merge main automatically or claim Actions success without run evidence.
