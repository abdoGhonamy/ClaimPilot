# Business Requirements Document

## Context and personas

ClaimPilot helps an insurance adjuster evaluate claims against the policy wording in force on the incident date. Adjusters submit and investigate claims; supervisors review material decisions; directors handle high-value or critical decisions; viewers inspect read-only information.

## Objectives and measurable criteria

| ID | Requirement and acceptance criterion | Status and evidence |
|---|---|---|
| BR-01 | Select the policy version effective on the incident date. | Implemented: `ApplicableVersionRule` and version-trap tests. |
| BR-02 | Compute deductibles, limits, coinsurance and payout in deterministic code. | Implemented: `DeterministicAdjudicationEngine`. |
| BR-03 | Return cited answers or refuse on insufficient evidence. | Implemented: `/api/ask`, `GroundednessChecks`. |
| BR-04 | Require human approval before a final decision. | Implemented: review queue and approval state machine. |
| BR-05 | Route, prioritize, escalate and measure human review work. | Implemented: assignment router, SLA worker and `/api/statistics`. |
| BR-06 | Let users inspect agent activity, tools, evidence and estimated cost by run. | Implemented: SSE and `/api/claims/runs/{runId}/trace`. |

## Business rules

1. The LLM never calculates a payable amount or finalizes a decision.
2. An approval action must match the item assignee and the reviewer authority threshold.
3. An unresolved or poorly evidenced answer must use the standard refusal.
4. Only synthetic/public data belongs in this repository.

## Out of scope

Production policy administration integration, payments, identity federation, legal retention schedules, and real customer data are deferred.

## Assumptions and risks

Synthetic policy wordings represent realistic structure but not legal advice. Local Ollama availability affects answer quality and latency. The implemented MVP uses in-process rate controls and local storage; the System Design gap table records production replacements.

## Traceability

| BR | Evidence |
|---|---|
| BR-01/02 | `ApplicableVersionRuleTests`, `DeterministicAdjudicationEngineTests` |
| BR-03 | `RefusalGroundednessTests`, `EvaluationCorpus` |
| BR-04/05 | `ApprovalServiceStateMachineTests`, `ApprovalAuthorityFilterTests` |
| BR-06 | `ClaimsController.Trace`, `RunTraceViewBuilder` |
