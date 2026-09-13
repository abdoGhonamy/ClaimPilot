# ClaimPilot Architecture

## System overview

```
                 ┌──────────────────────────────────────────────────┐
   Claim/Ask     │                     ClaimPilot API                │
 ───────────────▶│  Auth (JWT, roles) ➜ Controllers                  │
                 │    │                                             │
                 │    ▼                                             │
                 │  SupervisorOrchestrator (multi-agent, SSE trace)  │
                 │    ├─ Evaluator        (claim info + anomalies)   │
                 │    ├─ Coverage Matcher (retrieve policy version)  │
                 │    ├─ Exclusion Analyst(LLM selects candidates,   │
                 │    │                   check_exclusion verifies)  │
                 │    ├─ DeterministicAdjudicationEngine             │
                 │    │    (pure math: deductible ➜ coinsurance ➜    │
                 │    │     limit ➜ payable; exclusions ➜ 0)         │
                 │    ├─ Adjudication Drafter(LLM rationale ONLY)    │
                 │    │    └─ draft_adjudication (gated write,       │
                 │    │       IsFinal=false, never final)            │
                 │    └─ ApprovalItem ➜ review queue                 │
                 │                                                    │
                 │  Review workflow (Supervisor approves ➜ Decision   │
                 │  finalized, DecisionLetter issued, audit log)     │
                 │                                                    │
                 │  AskService (RAG): retrieve ➜ grounded LLM answer  │
                 │    with exact refusal + number grounding           │
                 └────────┬───────────────────┬──────────────┬────────┘
                          ▼                   ▼              ▼
                 Postgres+pgvector      Redis (cache,     Ollama (llama3.2,
                 (corpus, runs,         SLA escalation    nomic-embed-text)
                  decisions, audit)     workers)
```

## Layering (Clean Architecture)

- `ClaimPilot.Domain` — entities, enums, value objects, **pure rules**:
  `ApplicableVersionRule`, refusal string constant, policy version statuses.
- `ClaimPilot.Application` — orchestration/agents, telemetry interfaces, review service,
  deterministic engine, AskService. No EF, no HTTP.
- `ClaimPilot.Infrastructure` — EF Core + pgvector, Ollama providers, tool registry,
  Redis cache/workers, seeding, migrations.
- `ClaimPilot.API` — controllers, auth, SSE, health, middleware, DI composition.

## Determinism & the LLM boundary

| Concern | Owner | Never |
|---|---|---|
| Payable amounts, deductibles, limits, coinsurance | `DeterministicAdjudicationEngine` | LLM |
| Policy version selection | `ApplicableVersionRule` (incident-date pinned) | LLM / newest-version default |
| Refusal wording | `AskService` (exact constant) | LLM |
| Final decision | Supervisor (`approve`), idempotent + audited | draft tool / LLM |

The LLM receives only: claim facts, the matched version block, retrieved excerpts, and the
engine's numeric result. Its output (rationale, exclusion candidate selection, citations) is
parsed with the tolerant `JsonExtraction.DeserializeArray`, and every tool call is written to
the run trace.

## Key invariants

1. `draft_adjudication` persists a `Decision` with `IsFinal=false`. Approval sets
   `IsFinal=true`, links the `ApprovalItem`, sets `AdjudicationRun.FinalDecisionId`, issues a
   `DecisionLetter`, and records an audit row.
2. Version trap: `GetApplicableVersionAsync` = versions with
   `EffectiveDate <= incidentDate && Status != Retired`, newest-first — tested for AUT-2022
   (2023 → v1 $5,000; 2026 → v2 $7,200).
3. Audits are append-only (`AuditLogs`, `ApprovalHistories`, run `TraceRecords`).
4. Roles server-side: queue = Adjuster+Supervisor; viewer can only read;
   `approve`/`reject`/`edit` are gated to the item's `AssigneeRole` (see
   "Human review assignment" below) with monetary thresholds on approve/edit;
   `assign`/priority override = Supervisor+Director.

## Human review assignment

Every `ApprovalItem` carries a typed `AssigneeRole` (`Adjuster`, `Supervisor`,
`Director`) plus an `AssignedAt` timestamp. `AssignmentRouter.Compute` routes
items **at creation** in `SupervisorOrchestrator` by priority floor then amount
bands: Critical → Director; High → Supervisor (≤ $100K) / Director; Normal &
Low → Adjuster (≤ $10K) / Supervisor / Director. The enum is stored as a capped
string column (`AssignedTo varchar(32)`) with an index; the API serializes it
as JSON strings via `JsonStringEnumConverter`. `approve`/`reject`/`edit` are
authorized **assignee-only** by `RequireApprovalAuthority` (higher roles must
reassign first; unassigned items → 409). `ApprovalService.AssignAsync` sets
`AssignedTo` + `AssignedAt`; `EscalateAsync` forwards to the next level;
`SlaEscalationWorker` reassigns stalled items (8h Adjuster → Supervisor,
16h Supervisor → Director, 2h unassigned → by priority). See `docs/SECURITY.md`.

## Storage

- `PolicyChunks` carry a 768-dim `vector` column (HNSW index) + BM25 text index for hybrid
  retrieval (`IChunkRepository` + `RetrievalService`).
- Content hash dedupe is **per policy version** (composite `(PolicyVersionId, ContentHash)`)
  because boilerplate repeats across wordings.

## Pipeline & operations

- Startup: migrations ➜ identity seed (3 roles/users) ➜ `DemoDataSeeder` (idempotent corpus
  + 12 claims; embeddings via Ollama with a deterministic fallback).
- CI: build + unit tests + `docker compose config` validation (`.github/workflows/ci.yml`).
- Delivery: 8 PR templates under `.github/pull_request_template/`; releases tagged `vX.Y.Z`.