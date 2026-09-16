# Hands-on Lab: Trustworthy Insurance RAG

## Outcomes

Explain version-aware retrieval, run a claim workflow, inspect a trace, and apply an auditable approval decision.

## Steps and expected output

1. Run `docker compose up -d --build`; `/health` returns 200.
2. Import the Postman collection and sign in as `adjuster`; the token and Adjuster role appear.
3. Run the version-trap claim `CLAIM-2023-001`; the trace selects the 2022 policy version and proposes $5,000.
4. Ask an unsupported question; response sets `refused: true`.
5. Sign in as supervisor, inspect the queue, then explain why approval remains a human decision.

## Stretch challenges

1. Add a policy version and a regression case proving date selection.
2. Add an adversarial document and show the refusal behaviour.
3. Change assignment thresholds and add a test for the new boundary.

## Answer key

The version trap selects the effective version, deterministic code computes the payout, and the LLM only drafts rationale. A response without evidence must refuse. The authority filter blocks a reviewer who is not assigned to the item.
