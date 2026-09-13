# ClaimPilot — the whole system explained

> [!info] How to use this in Obsidian
> Open the repository folder as a vault (File → Open folder as vault), or copy this file into any vault. Every diagram is Mermaid — Obsidian renders it in "Reading view". Sections are ordered like a story for a video walkthrough.

---

## 0. One-sentence pitch

ClaimPilot is a **multi-agent AI co-pilot for insurance claim adjudication**: four LLM agents read the *right version* of a policy and decide *what* the rules say, a **deterministic (rule, zero-LLM) engine computes the money**, a human supervisor **must approve** before anything is final, and every step is **traceable and auditable**.

The mountain in one line:

```text
"Who reads things" = AI (agents, retrieval, drafting)
"Who does the math" = deterministic engine  <- the ONLY thing that can touch money
"Who says it's final" = a human (Supervisor role)
```

---

## 1. The mental model (tell this story)

```mermaid
flowchart LR
    P["A claim arrives (claim amount, policy number, incident date)"] --> V["1. Pin the right POLICY VERSION<br/>(by incident date — never 'latest')"]
    V --> C["2. Coverage Matcher agent<br/>-> what is covered?"]
    C --> E["3. Exclusion Analyst agent<br/>-> which exclusions apply?"]
    E --> M["4. DETERMINISTIC ENGINE<br/>-> computes the payout (pure math)"]
    M --> A["5. Anomaly Detector agent<br/>-> flags anything suspicious"]
    A --> D["6. Adjudication Drafter agent<br/>-> writes a human-readable decision<br/>(saved as a NON-FINAL draft)"]
    D --> H{"7. HUMAN REVIEW GATE"}
    H -- "Supervisor approves" --> L["Decision letter issued<br/>Decision IsFinal = true<br/>history + audit written"]
    H -- "Supervisor edits / rejects" --> R["Draft reworked or rejected<br/>(still not final)"]
    H -- "Adjuster tries to approve" --> X["403 Forbidden (role gate)"]
```

> [!important] 
> **Nothing is ever final without a human.** The `draft_adjudication` tool persists a draft *"Draft decision saved but NOT final. Awaiting human approval."* The system is explicitly designed so a fully autonomous pass can only ever produce a recommendation.

---

## 2. Big-picture architecture (Clean Architecture)

```mermaid
graph TD
    subgraph API["src/ClaimPilot.API — presentation (web layer)"]
        A1["ClaimsController<br/>POST /api/claims/{id}/adjudicate (SSE)"]
        A2["AskController<br/>POST /api/ask (Q&A)"]
        A3["ReviewController<br/>GET /api/review + approve/reject/edit/..."]
        A4["AuthController<br/>POST /api/auth/login (JWT)"]
    end

    subgraph APP["src/ClaimPilot.Application — use cases (no EF, no controllers)"]
        B1["SupervisorOrchestrator"]
        B2["Agents (4) + ToolRegistry"]
        B3["DeterministicAdjudicationEngine"]
        B4["AskService (grounded Q&A)"]
        B5["ApprovalService (state machine)"]
    end

    subgraph DOM["src/ClaimPilot.Domain — entities + rules (no dependencies)"]
        C1["Entities: Policy, Claim, Decision,<br/>ApprovalItem, Anomaly, ..."]
        C2["Enums / ValueObjects / Exceptions"]
    end

    subgraph INF["src/ClaimPilot.Infrastructure — implementation"]
        D1["AppDbContext (EF Core + Npgsql + pgvector)"]
        D2["Repositories"]
        D3["RetrievalService (hybrid: dense + keyword)"]
        D4["OLlama LLM + Embedding provider"]
    end

    subgraph EXT["External services"]
        E2[("PostgreSQL + pgvector<br/>HNSW index, 768-dim embeddings")]
        E3["Redis (distributed cache)"]
        E4["Ollama — llama3.2 (LLM)<br/>nomic-embed-text (embeddings)"]
    end

    API --> APP
    APP --> DOM
    APP -.->|"implemented by"| INF
    INF --> E2
    INF --> E3
    INF --> E4
```

**Dependency rule:** Domain knows nothing. Application depends on Domain and on *interfaces* defined in `Application/Interfaces`. Infrastructure implements those interfaces. The API wires everything together in `Program.cs`.

---

## 3. Runtime topology (how it's deployed)

```mermaid
flowchart LR
    subgraph host["One host (local or docker-compose)"]
        API["ClaimPilot.API<br/>(ASP.NET Core, port 5028 local / 8080 in Docker)"]
        PGV[("PostgreSQL 16 + pgvector<br/>port 5432 (local) / 5433 (compose)")]
        RDS[("Redis<br/>6379 / 6380")]
        OLL["Ollama<br/>11434 / 11435"]
    end
    BRO["Browser / client (curl / Postman)"] -->|"HTTPS + JWT Bearer"| API
    API -->|"EF Core → Npgsql"| PGV
    API -->|"StackExchange.Redis"| RDS
    API -->|"HTTP (Ollama API)"| OLL
```

- The same API binary runs locally and in Docker — the compose file merely remaps host ports so both stacks can coexist.
- LLM calls go **out** to Ollama (no cloud API): `llama3.2` answers, `nomic-embed-text` produces the 768-dim vectors that pgvector indexes.

---

## 4. The live story: Sequence diagram of an adjudication

```mermaid
sequenceDiagram
    autonumber
    participant API as API (ClaimsController)
    participant ORC as SupervisorOrchestrator
    participant DB as PostgreSQL
    participant A1 as CoverageMatcherAgent
    participant A2 as ExclusionAnalystAgent
    participant ENG as DeterministicEngine
    participant A3 as AnomalyDetectorAgent
    participant A4 as AdjudicationDrafterAgent
    participant OLL as Ollama LLM

    API->>API: open SSE stream (text/event-stream)
    API->>ORC: RunAsync(claimId, correlationId)
    ORC->>DB: create AdjudicationRun (status Running)
    ORC-->>API: event: workflow_started
    ORC->>DB: load claim + policy
    ORC->>DB: pin PolicyVersion for incident date (ApplicableVersionRule)
    ORC-->>API: event: policy_version_selected {version, effective_date}
    ORC->>A1: agent 1 — what is covered?
    A1->>OLL: tool call RetrievePolicyVersioned / ListCoverageItems
    A1-->>ORC: coverage set {limits, deductible, coinsurance}
    ORC-->>API: event: agent + tool events (per tool call)
    ORC->>A2: agent 2 — which exclusions apply?
    A2->>OLL: tool call CheckExclusion
    A2-->>ORC: applicable exclusion codes
    ORC->>ENG: Compute(claimAmount, deductible, coinsurance, limit, exclusions)
    ENG-->>ORC: Computation result (payout + step details)
    ORC->>DB: persist computation + records breakdown
    ORC-->>API: event: engine_completed {computed payout}
    ORC->>A3: agent 3 — any anomalies?
    A3->>OLL: tool call RecordAnomaly (gated write)
    A3-->>ORC: anomaly list
    ORC->>A4: agent 4 — draft a human-readable decision
    A4->>OLL: tool call DraftAdjudication (GATED WRITE)
    A4-->>ORC: decision JSON
    ORC->>DB: save Decision IsFinal=false
    ORC-->>API: event: agent events + draft saved (non-final)
    ORC->>DB: create ApprovalItem (status Pending)
    ORC-->>API: event: review_required
    ORC-->>API: run_complete {status, runId}
    API-->>client: SSE stream closes (drain + complete)
```

**How SSE works (ClaimsController, 82–140):** a `System.Threading.Channels.Channel` bridges the orchestrator's `EventRaised` C# event to the HTTP response; a drain task writes each serialized event (`event: <name>\ndata: {...}\n\n`) and flushes. The catch-all guarantees `event: error` is still sent on failure.

---

## 5. The four agents and their tools

```mermaid
flowchart LR
    subgraph tools["Tool registry (IToolRegistry) — who may call what"]
        T1["RetrievePolicyVersioned<br/>(read)"]
        T2["ListCoverageItems<br/>(read)"]
        T3["CheckExclusion<br/>(read)"]
        T4["RecordAnomaly<br/>(WRITE + audit)"]
        T5["DraftAdjudication<br/>(GATED WRITE — never auto-final)"]
    end

    AG1["CoverageMatcherAgent"] --> T1
    AG1 --> T2
    AG2["ExclusionAnalystAgent"] --> T3
    AG3["AnomalyDetectorAgent"] --> T4
    AG4["AdjudicationDrafterAgent"] --> T5

    AG1 -.->|"sequential, with retry+backoff"| AG2
    AG2 -.->|"then engine (no LLM)"| AG3
    AG3 -.->|"then"| AG4
    AG4 -.->|"then"| GATE["HUMAN REVIEW GATE"]

    style T5 fill:#fdd,stroke:#c00
    style T4 fill:#fdd,stroke:#c00
    style GATE fill:#ffd,stroke:#a80
```

| Agent | Job | Tools it may call |
|---|---|---|
| Coverage Matcher | "What does this policy *cover* for this claim type?" | `RetrievePolicyVersioned`, `ListCoverageItems` |
| Exclusion Analyst | "Which exclusions apply to this loss cause?" | `CheckExclusion` |
| (Deterministic Engine) | **Computes the payout — no LLM** | *none* |
| Anomaly Detector | "Is anything unusual (pending docs, variance, suspicious pattern)?" | `RecordAnomaly` |
| Adjudication Drafter | "Write a decisions-style recommendation a human can read." | `DraftAdjudication` |

- The **orchestrator** (SupervisorOrchestrator) runs the agents **in order**, wraps each in retry + exponential backoff (default: 3 tries, 500 ms base), enforces an **iteration cap** and a **5-minute run timeout**, and subscribes to the event sink.
- **Fallback:** if orchestration fails, it degrades to *safe plain RAG* rather than inventing an answer.

---

## 6. The deterministic engine — where the money math lives

This is the **only component allowed to touch money**. It's a pure function: same inputs ⇒ same output (tests assert determinism).

```mermaid
flowchart TD
    S["Start: claimed amount X"] --> E{Any applicable<br/>exclusion code?}
    E -- "yes" --> Z["PAYOUT = $0<br/>InsufficiencyReason: 'Claim excluded by policy exclusion(s): …'<br/>FINAL — no further math"]
    E -- "no, and X ≤ 0" --> Z2["PAYOUT = $0 (invalid claim)"]
    E -- "no, X > 0" --> D["X -= deductible<br/>(if deductible defined)"]
    D --> C["X = X × coinsurance rate<br/>(if coinsurance % defined)"]
    C --> L["X = min(X, coverage limit)<br/>(if limit defined)"]
    L --> R["PAYOUT = max(X, 0)"]
    R --> O["Output: payout + per-step breakdown<br/>(claimed → deductible → coinsurance → cap)"]

    style Z fill:#fdd,stroke:#c00
    style O fill:#dfd,stroke:#080
```

**Worked example on the seed corpus:**

| Claim | Incident | Pinned version | Math | Payout |
|---|---|---|---|---|
| CLAIM-2023-001 (AUT-2022) | 2023 | **v1** (limit $5,000, deductible $500) | 6200 − 500 = 5700 → min(5700, 5000) | **$5,000** |
| CLAIM-2026-001 (AUT-2022) | 2026 | **v2** (limit $10,000, deductible $600) | 7800 − 600 = 7200 → min(7200, 10000) | **$7,200** |

> [!tip] Version-trap design
> The same policy has two versions. v2 has a **higher** limit ($10k). If the system naively used "latest version", CLAIM-2023-001 would wrongly pay $9,500 under v2. The `ApplicableVersionRule` selects the version whose `EffectiveDate` is **≤ the incident date** (never "newest"), so the 2023 claim correctly pays only $5,000. Live E2E and unit tests both verify this.

---

## 7. Grounded Q&A (`/api/ask`) — the refusal guard

The ask flow answers policy questions but **refuses** anything the policy can't back up (anti-hallucination).

```mermaid
flowchart TD
    Q["Question + policy number + incident date (AskController)"] --> PIN["Pin policy version<br/>(same ApplicableVersionRule)"]
    PIN --> RETR["Hybrid retrieval over that version's chunks"]
    RETR --> SUF{Sufficient evidence<br/>(retrieved context)?}
    SUF -- "no" --> REF["Return EXACT refusal string"]
    SUF -- "yes" --> LLM["LLM answers questions (not money)"]
    LLM --> N{Answer mentions<br/>a number/payout?}
    N -- "number NOT found in corpus" --> REF2["Refuse with EXACT string"]
    N -- "number IS grounded in corpus" --> OK["Answer passed through"]

    style REF fill:#fdd,stroke:#c00
    style REF2 fill:#fdd,stroke:#c00
    style OK fill:#dfd,stroke:#080
```

The exact refusal string is a constant:
```text
Not enough information in the policy corpus to determine this.
```
Prompt-injection text inside an excerpt is treated as **data, not instructions** — if an adversarial chunk tries to "coax" a different payout, the groundedness check refuses (`GroundednessChecks.ContainsUnsupportedNumbers` scans the answer tokens for numbers absent from the retrieved chunks).

```mermaid
sequenceDiagram
    autonumber
    participant C as AskController
    participant S as AskService
    participant R as RetrievalService
    participant L as LLM
    C->>S: AskRequest {question, policyNumber, incidentDate}
    S->>R: RetrieveAsync(query pinned to version)
    R-->>S: chunks (+ scores) or insufficient
    alt insufficient context
        S-->>C: { answer = EXACT refusal, refused = true }
    else enough context
        S->>L: answer (prompt says "never compute money")
        L-->>S: response text
        alt response contains unsupported number
            S-->>C: { answer = EXACT refusal, refused = true }
        else grounded
            S-->>C: { answer = response, refused = false }
        end
    end
```

---

## 8. Hybrid retrieval — dense vectors + keywords fused

```mermaid
flowchart LR
    Q["Question / agent need"] --> EMB["Embed with nomic-embed-text<br/>(768-dim)"]
    EMB --> DENSE["Dense: pgvector cosine distance<br/>over HNSW index (version-scoped)"]
    DENSE --> F["Reciprocal Rank Fusion (RRF)<br/>Score += 1/(k + rank)"]
    Q --> KW["Keyword: ILIKE full-text scan<br/>(version-scoped)"]
    KW --> F
    F --> TOP["Top-K chunks → agents / answerer"]
    DENSE -- "failure" --> FB["Fallback to keyword-only<br/>(system stays functional)"]
    FB --> TOP
```

- **Failure handling:** if dense retrieval throws (e.g. missing embedding), the service logs it, falls back to keyword-only, and only surfaces an error if *both* paths return nothing.
- Chunks carry one embedding each and belong to a `PolicyVersion`, so **retrieval is always version-scoped** — a 2023 question never sees 2026 clauses.

---

## 9. Data model (the entities)

```mermaid
erDiagram
    POLICY ||--o{ POLICYVERSION : "has versions"
    POLICYVERSION ||--o{ COVERAGEITEM : "defines"
    POLICYVERSION ||--o{ EXCLUSION : "defines"
    POLICYVERSION ||--o{ POLICYCHUNK : "stores text as vectors"
    POLICY ||--o{ CLAIM : "is cited by"
    CLAIM ||--o{ CLAIMDOCUMENT : "has"
    CLAIM ||--o{ ADJUDICATIONRUN : "is adjudicated by"
    POLICYVERSION ||--o{ ADJUDICATIONRUN : "pinned to run"
    ADJUDICATIONRUN ||--o{ AGENTRUN : "executes"
    ADJUDICATIONRUN ||--o| APPROVALITEM : "ends in a review item"
    ADJUDICATIONRUN ||--o{ ANOMALY : "may flag"
    ADJUDICATIONRUN ||--o| DECISION : "produces one draft"
    APPROVALITEM ||--o{ APPROVALHISTORY : "records state moves"
    APPROVALITEM o|--|| DECISION : "finalized decision"
    DECISION ||--o| DECISIONLETTER : "generates letter on approval"
    AUDITLOG }o--|| ADJUDICATIONRUN : "audit trail"
    TRACERECORD }o--|| ADJUDICATIONRUN : "step trace"
    USAGERECORD }o--|| ADJUDICATIONRUN : "LLM usage"

    DECISION {
        bool IsFinal "draft until human approves"
    }
    APPROVALITEM {
        ApprovalStatus status "Pending/Approved/Rejected/..."
    }
```

All persisted via EF Core + Npgsql in `AppDbContext` (16 tables). `DecisionLetters` describe *how* and store the amount text issued to the customer.

---

## 10. Human review workflow (state machine)

```mermaid
stateDiagram-v2
    [*] --> DraftNonFinal: adjudication run saves Decision (IsFinal=false)
    DraftNonFinal --> Pending: ApprovalItem created (queue default = Pending)
    Pending --> Approved: [Supervisor only] approve
    Pending --> Rejected: [Supervisor only] reject
    Pending --> Edited: [Supervisor only] edit (decision recomputed/drafted)
    Edited --> Pending: revised draft awaiting re-decision
    Pending --> ReReviewed: [Supervisor only] re-review (re-opens)

    note right of Approved
        • Decision.IsFinal = true
        • run.FinalDecisionId set
        • DecisionLetter issued
        • audit entry "Finalised"
    end note
    note left of Rejected
        • rejection recorded
        • nothing becomes final
    end note
    Approved --> [*]
```

- The queue endpoint is `GET /api/review` and **defaults to Pending** when no status filter is given.
- **Role gating:** approve/reject/edit/re-review/assign/escalate/priority are all `[Authorize(Roles = "Supervisor")]`. An Adjuster attempting to approve gets **403** (verified in E2E). Queue listing is available to Adjuster + Supervisor; Viewer is read-only.

---

## 11. Security, roles, and audit

```mermaid
flowchart LR
    LOGIN["POST /api/auth/login<br/>user + demo password '#2026-local-only'"] --> JWT["JWT bearer token<br/>claims: role = Adjuster | Supervisor | Viewer"]
    JWT --> API["API actions"]
    API --> GA{"[Authorize(Roles=…)] gate<br/>at the controller layer"}
    GA -- "ok" --> ACT["Action executes"]
    GA -- "no" --> F["403 Forbidden"]
    ACT --> AUD["AuditService appends AuditLog<br/>(append-only — e.g. 'Finalised')"]
    ACT --> TRACE["Orchestration trace: every step<br/>written to TraceRecords with correlation id"]
```

---

## 12. Code map — where everything lives

```mermaid
flowchart LR
    subgraph src["src/"]
        subgraph dom["Domain"]
            DM1["Entities/  (Policy, Claim, Adjudication, Approval, Coverage)"]
            DM2["Enums/ ValueObjects/ Exceptions/"]
        end
        subgraph app["Application"]
            AP1["Services/SupervisorOrchestrator.cs — the agent loop"]
            AP2["Services/Agents/  (4 agents)"]
            AP3["Services/DeterministicAdjudicationEngine.cs — money math"]
            AP4["Services/AskService.cs — grounded Q&A"]
            AP5["Services/ApprovalService.cs — finalize on approve"]
            AP6["Domain/Services/ApplicableVersionRule.cs — version pinning"]
            AP7["Common/GroundednessChecks.cs, JsonExtraction.cs"]
            AP8["Interfaces/  (IAgent, IRepositories, IToolRegistry, IAdjudicationEngine, …)"]
        end
        subgraph inf["Infrastructure"]
            IF1["Data/AppDbContext.cs — EF Core + pgvector"]
            IF2["Repositories/"]
            IF3["Services/RetrievalService.cs — hybrid RRF"]
            IF4["Data/Seed/  (DemoDataSeeder, CorpusSpec, EvaluationCorpus)"]
        end
        subgraph api["API"]
            API1["Controllers/  (Claims/SSE, Review, Ask, Auth)"]
            API2["Program.cs — DI, JWT, OpenAPI"]
        end
    end
    api --> app
    app --> dom
    dom -.-> inf
    inf -. "implements app interfaces" .-> app
```

Key files and what to point at in the video:
| File | Say this |
|---|---|
| `Application/Services/SupervisorOrchestrator.cs` | The conductor: version pinning, agent order, engine, review gate, retries/timeouts |
| `Application/Services/DeterministicAdjudicationEngine.cs` | The trust anchor: pure math, no LLM, fully unit-tested |
| `Application/Interfaces/Orchestration/IAgent.cs` | The tool contract — `ToolName` enum + gated-write flags (`IsWrite`, `IsGatedWrite`, `RequiresAudit`) |
| `Application/Services/Agents/AdjudicationDrafterAgent.cs` | The drafter that only ever writes a **non-final** draft |
| `Domain/Services/ApplicableVersionRule.cs` | Why 2023 ≠ 2026 payouts on the same policy |
| `Infrastructure/Services/RetrievalService.cs` | Dense (pgvector/HNSW) + keyword + RRF fusion, version-scoped |
| `API/Controllers/ClaimsController.cs` | SSE endpoint that streams live agent activity |
| `API/Controllers/ReviewController.cs` | The human gate; Supervisor-only actions |
| `Data/Seed/EvaluationCorpus.cs` | The 26-case eval dataset incl. version traps + injection cases |

---

## 13. Tests (what proves it works)

```mermaid
flowchart LR
    T["tests/ClaimPilot.Tests — 37 tests, all passing"] --> T1["DeterministicAdjudicationEngineTests<br/>determinism + traps + injection"]
    T --> T2["ApplicableVersionRuleTests<br/>version selection logic"]
    T --> T3["RefusalGroundednessTests<br/>exact refusal, unsupported payouts, injection"]
    T --> T4["JsonExtractionTests<br/>robust JSON parse for LLM output"]
    T --> T5["ApprovalServiceStateMachineTests<br/>approve finalizes + letter + audit"]
    T --> T6["ReviewRoleGuardTests<br/>Supervisor-only endpoints (reflection)"]
    T ==> CI[".github/workflows/ci.yml<br/>restore → build → test → compose validate"]
```

---

## 14. The non-negotiables (design invariants)

> [!warning] 8 rules the whole design obeys
> 1. **The LLM never computes money.** Only `DeterministicAdjudicationEngine` does. The engine has no LLM dependency.
> 2. **Version is pinned by incident date, never "latest".** `ApplicableVersionRule`; retrieval is version-scoped.
> 3. **Refusal string is exact and constant**: `Not enough information in the policy corpus to determine this.`
> 4. **`draft_adjudication` is a gated write.** It persists `IsFinal=false`; only a Supervisor approval flips it true.
> 5. **Roles are enforced server-side** (`Supervisor` for finalizing), not just in the UI.
> 6. **Audit + trace are append-only** records of every step and decision.
> 7. **No secrets in the repo** (`.env` is gitignored; the seed login is a documented demo-only password).
> 8. **Determinism is tested** — same inputs, same payout, every time.

---

## 15. How to run it (cheat sheet)

```bash
# Local dev (already up): Postgres+pgvector, Redis, Ollama
dotnet run --project src/ClaimPilot.API            # http://localhost:5028

# Containerized stack (docker-compose.yml): remaps ports to 5433/6380/11435/8080
docker compose up -d --build                       # http://localhost:8080

# Tests
dotnet test tests/ClaimPilot.Tests/ClaimPilot.Tests.csproj   # 37 passing

# Demo users (local seed): adjuster / supervisor / viewer
# password for all: #2026-local-only  (POST /api/auth/login)
```

---

## 16. Glossary

| Term | Meaning |
|---|---|
| Adjudication Run | One full pass over a claim: version pin → agents → engine → draft → review item |
| Agent | An LLM-driven worker with a narrow job + a fixed set of tools |
| Tool | A capability the agent can invoke; writes are flagged and gated |
| Gated write | A tool that persists but never finalizes (needs human approval) |
| Policy version | A snapshot of a policy's terms with an `EffectiveDate` |
| Deterministic engine | Pure calculation of payout from policy numbers + claim amount |
| Review item / ApprovalItem | The human queue record created for each completed run |
| Groundedness | Guard that an answer's numbers actually come from the retrieved corpus |
| RRF | Reciprocal Rank Fusion — combining dense + keyword ranked lists |
| SSE | Server-Sent Events — the live stream of agent activity to the browser |