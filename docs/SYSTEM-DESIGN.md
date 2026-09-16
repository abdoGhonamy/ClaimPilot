# System Design Document

## Part A: target architecture

An internet-facing gateway terminates TLS, applies managed rate limiting and forwards authenticated requests to a stateless API. A broker runs durable, cancellable jobs. A managed relational database and vector database retain domain state and embeddings; object storage holds documents. A secrets manager supplies provider credentials. OpenTelemetry exports traces, metrics and logs to a managed observability stack. CI/CD promotes immutable images through test, staging and production, with encrypted backups and recovery drills.

## Part B: implemented MVP and gaps

| Target component | Implemented? | Interim mitigation | Gap to close |
|---|---|---|---|
| API gateway/rate limiting | Partial | ASP.NET payload limits and authenticated endpoints | Add distributed limiter at gateway; about 4 hours plus hosting cost. |
| Durable job broker | No | In-request SSE orchestration with cancellation token | Add Redis-backed durable queue, resume and idempotency keys; about 12 hours. |
| Managed secrets | No | `.env.example`; development-only defaults | Use cloud secret manager and rotate deployment values; about 3 hours. |
| Managed vector store | No | PostgreSQL with pgvector/HNSW | Migrate adapter to managed Postgres/vector offering; about 8 hours. |
| Observability platform | Partial | Persisted traces, usage and audit records | Export OpenTelemetry traces/metrics; about 6 hours. |
| Provider resilience | Partial | Local Ollama and deterministic embedding fallback | Add hosted provider, routing and health-based fallback; about 10 hours. |

## Significant decisions

Clean Architecture keeps policy rules independent of HTTP, EF Core and model providers. PostgreSQL plus pgvector keeps relational claim state and vector retrieval together for the MVP. The supervisor pipeline makes ordering, retries and auditability explicit. Deterministic payout calculation protects against arithmetic hallucination. See `docs/adr/` for alternatives and decisions.
