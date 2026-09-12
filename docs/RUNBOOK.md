# ClaimPilot Runbook

## Local prerequisites

| Component | Version / image | Notes |
|---|---|---|
| .NET SDK | 10.0.x | `dotnet --version` |
| PostgreSQL | 16 + **pgvector** | `pgvector/pgvector:pg16` recommended |
| Redis | 7 | for cache + SLA workers |
| Ollama | latest | `ollama pull llama3.2` and `nomic-embed-text` |

### 1. Start infra containers

```bash
docker run -d --name claimpilot-pg \
  -e POSTGRES_DB=claimpilot -e POSTGRES_USER=claimpilot -e POSTGRES_PASSWORD=claimpilot \
  -p 5432:5432 pgvector/pgvector:pg16

docker run -d --name claimpilot-redis -p 6379:6379 redis:7-alpine

docker run -d --name claimpilot-ollama -p 11434:11434 ollama/ollama:latest
docker exec claimpilot-ollama ollama pull llama3.2
docker exec claimpilot-ollama ollama pull nomic-embed-text
```

Create the vector extension once as a superuser (the migration re-runs it harmlessly):

```bash
docker exec claimpilot-pg psql -U postgres -d claimpilot -c "CREATE EXTENSION IF NOT EXISTS vector;"
docker exec claimpilot-pg psql -U postgres -d claimpilot -c "GRANT ALL ON SCHEMA public TO claimpilot;"
```

### 2. Run the API

```bash
dotnet restore ClaimPilot.slnx
dotnet build ClaimPilot.slnx
dotnet run --project src/ClaimPilot.API
```

On startup the API migrates the DB and seeds the corpus + demo claims. The first seed invokes
Ollama embeddings (~a few minutes if the model is still warm); if Ollama is unreachable the
seeder falls back to a deterministic zero vector (allocation still succeeds). API:
`http://localhost:5028`, OpenAPI at `/swagger`.

## Adjudication walkthrough (SSE)

```bash
TOKEN=$(curl -s http://localhost:5028/api/auth/login -X POST \
  -H "Content-Type: application/json" \
  -d '{"username":"adjuster","password":"Adjuster#2026-local-only"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['token'])")

curl -s -N -X POST http://localhost:5028/api/claims/{CLAIM-ID}/adjudicate \
  -H "Authorization: Bearer $TOKEN"
```

Streamed events: `workflow_started`, `policy_version_selected`, `agent_started` /
`agent_completed` per agent, `engine_completed`, `review_required`, `completed` (or `error`).

### Verify a version trap

```bash
# CLAIM-2023-001 (AUT-2022) -> PolicyVersion 1, proposed_payout 5000
curl -s http://localhost:5028/api/claims | python3 -m json.tool
# …take the claim id…

# Same policy, incident 2026 -> PolicyVersion 2, proposed_payout 7200
```

### Approve the draft (supervisor only)

```bash
SUP=$(curl -s http://localhost:5028/api/auth/login -X POST \
  -H "Content-Type: application/json" \
  -d '{"username":"supervisor","password":"Supervisor#2026-local-only"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['token'])")

curl -s -X POST "http://localhost:5028/api/review/{ITEM-ID}/approve" \
  -H "Authorization: Bearer $SUP" -H "Content-Type: application/json" \
  -d '{"comment":"Approved as stated"}'
```

Approval finalizes the decision, issues the letter, and writes audit rows. Verify:

```bash
docker exec claimpilot-pg psql -U postgres -d claimpilot \
  -c "SELECT \"DecisionType\",\"IsFinal\",\"ApprovedAmount\" FROM \"Decisions\";"
docker exec claimpilot-pg psql -U postgres -d claimpilot \
  -c "SELECT \"LetterText\" FROM \"DecisionLetters\" ORDER BY \"IssuedAt\" DESC LIMIT 1;"
```

## Tests

```bash
dotnet test tests/ClaimPilot.Tests/ClaimPilot.Tests.csproj
```

Unit tests (37) need no external services: engine determinism + traps, `ApplicableVersionRule`,
refusal string + groundedness guards, approval state machine, role gating (reflection), and
`JsonExtraction` tolerance.

## Troubleshooting

- **`CREATE EXTENSION` permission denied** — create `vector` as the `postgres` superuser first
  (init script provided under `docker/initdb/`).
- **`Cannot write DateTime with Kind=Unspecified`** — persist/compare dates as UTC
  (`DateTime.SpecifyKind(v, DateTimeKind.Utc)`); Npgsql needs a kind for `timestamptz`.
- **SSE hangs / zero bytes** — the run faulted before the first event; check the API log for
  the agent failure and, after fixing, re-run the adjudication.
- **Embeddings absent** — confirm `nomic-embed-text` is pulled; the seeder falls back to zero
  vectors, so restart the seed (`dotnet run …`) once Ollama is healthy.
- **Duplicate chunk hash** — the hash is unique per policy version; rewordings share boilerplate
  legitimately.

## Container stack (compose)

```bash
cp .env.example .env
docker compose up -d --build
docker compose ps
```

API on `http://localhost:8080`, Postgres on host port 5433, Redis 6380, Ollama 11435 — chosen
to coexist with the local-development stack on 5432/6379/11434.

## Releases

- Cut `main` clean, verify build+tests, then:
  `git tag -a v1.0.0 -m "v1.0.0"` and push with `--tags`.
- Emergency hotfixes go through the `hotfix` PR template.