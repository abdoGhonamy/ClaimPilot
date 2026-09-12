# Changelog

All notable changes are captured by the tagged release commits. Plan milestones:

## [Unreleased]
### Added
- Deterministic payout engine, hybrid RAG RetrievalService, multi-agent SSE orchestration.
- Human review queue with SLA escalation, audit trail, decision letters.
- JWT auth + roles (Adjuster / Supervisor / Viewer), gated draft tool.
- Synthetic corpus (15 policies, 17 wordings, 42 coverage rows, 42 exclusions, 103 chunks),
  12 demo claims including two AUT version-trap claims, and a 26-case evaluation dataset.
- Unit test suite (37 tests) covering determinism, version traps, refusal/groundedness,
  approval state machine, role gating, JSON tolerance.

### Fixed
- SSE deadlock when a run faults before the first event.
- Detached-entity writes on draft/anomaly tools; timestamptz DateTime kind handling.
- Global-unique chunk content hash replaced by per-version uniqueness.

## [1.0.0] — 2026-09-11
First release: end-to-end adjudication verified live against Postgres+pgvector, Redis,
and Ollama (version traps resolve to $5,000 / $7,200; supervisor approval finalizes the
decision and issues a letter). Container stack, CI, docs, and PR templates included.