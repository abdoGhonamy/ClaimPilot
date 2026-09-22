# Evaluation Report

## Dataset

`EvaluationCorpus` contains 26 cases: retrieval, computation, version traps, exclusions, refusals, and six prompt-injection attacks. It exceeds the required 25 cases and includes more than five adversarial cases.

## Running the harness

Start the API and run the first-class .NET harness. It loads all 26 cases from
`EvaluationCorpus`, logs in with the local adjuster account, invokes `/api/ask`, evaluates
answer text, refusal correctness and citations, writes a timestamped JSON report, and exits
non-zero when any case fails.

```bash
dotnet run --project tools/ClaimPilot.Evaluation -- --base-url http://localhost:8080
```

Use `--category PromptInjection` for the adversarial subset or `--report artifacts/baseline.json`
to choose the report file.

## Adjudication-agent evaluation

The harness also evaluates the real SSE endpoint used by the adjudication workflow:

```bash
dotnet run --project tools/ClaimPilot.Evaluation -- --mode adjudication --base-url http://localhost:8080
```

It creates three isolated claims and calls `POST /api/claims/{claimId}/adjudicate` for each.
The report verifies the structured `agent_completed` SSE output of the three specialists:

1. **Coverage Matcher** selects the policy version effective on the incident date.
2. **Exclusion Analyst** detects the seeded `EX-9` mechanical-breakdown exclusion.
3. **Anomaly Detector** flags the deterministic `high_claim_ratio` heuristic.

The report includes every observed SSE event, so a failure can be traced to a specific agent
rather than being reported as a generic endpoint failure. The command exits non-zero if any
agent is absent, emits an error event, or violates its case's ground truth.

## Metrics

The harness reports answer-substring hit rate, refusal correctness, citation presence, and injection/refusal outcomes. Record the generated baseline below before submission; do not replace it with predicted figures.

| Run date | Model | Hit rate | Refusal correctness | Citation rate | Notes |
|---|---:|---:|---:|---:|---|
| Not yet recorded | Local Ollama | Pending | Pending | Pending | Run after corpus expansion and keep failures. |

## Failure analysis

Short synthetic clauses and unavailable embeddings can reduce semantic recall. Prompt injection remains a regression risk, so every injection result must be reviewed. A low score is evidence to improve chunking, metadata filters, or prompt safeguards rather than to hide the case.
