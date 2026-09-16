# ClaimPilot — Insurance Claims Adjudication Co-Pilot

A .NET 10 / ASP.NET Core claims co-pilot that drafts adjudication decisions with a
**deterministic payout engine**, hybrid **RAG (pgvector + BM25)** over a synthetic policy
corpus, and an **Ollama LLM** that never computes money. Every decision is gated behind a
human **review queue** with SLA escalations; the draft is only ever finalized by a
supervisor.

## 5-minute demo path

1. Run `docker compose up -d --build`, then open `http://localhost:8080`.
2. Sign in as `adjuster`, load claims, and select `CLAIM-2023-001`.
3. Ask a policy question and inspect its citations; ask an unsupported question and show refusal.
4. Run adjudication and show the live SSE trace, version selection, and deterministic payout.
5. Sign in as `supervisor`, inspect the review queue, statistics, and audit trace.

For a fully offline safe fallback set `AI__Provider=Deterministic`. It returns the
standard refusal rather than fabricating policy content. Ollama remains the normal local
provider; both implementations sit behind `ILLMProvider` and are selected by configuration.

## Why it is trustworthy

- Amounts are computed by `DeterministicAdjudicationEngine` (pure, repeatable, unit-tested).
  The LLM only drafts rationale text — it can never change a payout.
- A claim is priced against the **policy version in force on the incident date**, never the
  newest version (`ApplicableVersionRule`, included as regression tests).
- Ask the assistant anything: it either answers from the corpus with citations or refuses
  (exactly) `Not enough information in the policy corpus to determine this.`
- Prompt-injection is mitigated with data/excerpt framing, groundedness number checks, and
  a "corpus is data, not instructions" system contract (covered by tests).
- Roles are enforced server-side: **Adjuster** runs adjudications, **Supervisor** approves /
  rejects / edits final decisions, **Viewer** only reads.

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Quick start — containers

Requires Docker with Compose v2.

```bash
cp .env.example .env          # defaults match the repo
docker compose up -d --build
docker compose ps               # wait until api is healthy
```

Then:

```bash
curl -s http://localhost:8080/health
curl -s http://localhost:8080/swagger/index.html   # (Development mode)
```

Demo accounts: adjuster / `Adjuster#2026-local-only`, supervisor / `Supervisor#2026-local-only`,
director / `Director#2026-local-only`, viewer / `Viewer#2026-local-only`.

On first start the API migrates the database and seeds 15 policies, 17 wordings
(42 coverage rows / 42 exclusions / 103 vector chunks) and 12 demo claims, including two
**version-trap** claims used to prove date-correct pricing:

| Claim | Incident | Expected payout |
|-------|----------|-----------------|
| CLAIM-2023-001 (AUT-2022) | 2023-03-10 | **$5,000** (v1 limit, not the v2 $10,000) |
| CLAIM-2026-001 (AUT-2022) | 2026-01-20 | **$7,200** (v2: 7,800 − 600 deductible) |

## Quick start — local (vs the container stack above)

Requires .NET 10 SDK, Postgres 16 + pgvector, Redis 7, and Ollama with
`llama3.2` and `nomic-embed-text`:

```bash
dotnet restore ClaimPilot.slnx
dotnet build ClaimPilot.slnx
dotnet run --project src/ClaimPilot.API   # http://localhost:5028
```

The API migrates and seeds on startup. See [docs/RUNBOOK.md](docs/RUNBOOK.md) for the full
local setup and an adjudication walkthrough.

## Claim intake

Adjusters (and supervisors) create claims and attach supporting documents before
adjudication. Both endpoints require `Authorization: Bearer <token>` and the
`Adjuster` or `Supervisor` role.

### Create a claim

```bash
curl -s -X POST http://localhost:5028/api/claims \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d '{
    "policyNumber": "AUT-2022",
    "incidentDate": "2024-06-15T00:00:00Z",
    "claimAmount": 1400.00,
    "description": "My car was hit by another vehicle at the intersection of Main and 5th."
  }'
```

Returns `201` with the claim and a `Location` header. Claim numbers follow
`CLAIM-{year}-{seq}` (per-year sequential, generated inside the repository in a
serializable transaction). The policy must exist; incident date must be in the past;
amount is `> 0` and ≤ 10,000,000; description is 20–4000 characters.

### Upload a document

```bash
curl -s -X POST http://localhost:5028/api/claims/{claimId}/documents \
  -H "Authorization: Bearer $TOK" \
  -F "documentType=PoliceReport" \
  -F "file=@police_report.pdf;type=application/pdf"
```

Multipart with `documentType` (`PoliceReport`, `Photo`, `Receipt`, `MedicalReport`,
`Invoice`, `Other`) and `file`. Limits: 20 MB; extensions `.pdf` `.jpg`/`.jpeg`
`.png` `.webp` `.heic` with matching magic bytes (the client-declared `Content-Type`
is ignored — the actual file bytes are sniffed). Files are written outside `wwwroot`
to `{Storage:UploadRoot}/{claimId:N}/{documentId:N}{ext}` (generated names, never the
client filename), and each upload is recorded as a `ClaimDocument` row plus an
`Uploaded` audit entry.

### Storage configuration

`appsettings.json` → `Storage:UploadRoot` (default `uploads/`, git-ignored):

```json
{ "Storage": { "UploadRoot": "uploads" } }
```

## Test

```bash
dotnet test tests/ClaimPilot.Tests/ClaimPilot.Tests.csproj
```

## Evaluation, teaching, and submission evidence

Run the evaluation harness after starting the API:

```bash
dotnet run --project tools/ClaimPilot.Evaluation -- --base-url http://localhost:5028
```

The teaching deck, lab, assessment map and trainee-mistakes note live in `teaching/`.
Use `docs/SUBMISSION-CHECKLIST.md` for the GitHub configuration and recorded-video steps
that require the repository owner. Add the two unlisted video links here before submission.

Unit tests need no external services (determinism, version traps, refusal/groundedness,
approval state machine, role gating, JSON extraction).

## Project layout

```
src/ClaimPilot.API          Web API: controllers, SSE adjudication, auth, health
src/ClaimPilot.Application  Agents, orchestration, deterministic engine, review service
src/ClaimPilot.Domain       Entities, enums, value objects, pure rules
src/ClaimPilot.Infrastructure EF Core + pgvector, Ollama, Redis, tools, seeding, migrations
src/ClaimPilot.Tests        xUnit + FluentAssertions unit tests
docker/                     pgvector init, Dockerfile, compose
.github/                    CI workflow + PR templates
```

## License

Local demo/teaching project. Demo credentials and the signing key are local-only and must be
replaced in real deployments (see `.env.example`).
