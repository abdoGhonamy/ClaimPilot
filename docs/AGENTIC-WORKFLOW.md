# Agentic Development Workflow

ClaimPilot treats AI tooling as a governed contributor. Project instructions protect Clean Architecture boundaries; the prompt assets in `prompts/` make product prompts reviewable; independent review prompts cover security, testing and documentation; CI runs build and tests; and `AI-USAGE-LOG.md` records delegation, verification and mistakes.

## Controls used

1. Architecture rules: Domain/Application do not depend on web, LLM or vector SDKs.
2. Versioned prompt assets: product prompts are reviewed as files.
3. Scoped review roles: security, test and documentation reviews have separate checklists.
4. Repeated commands: Docker Compose, tests and the evaluation harness are documented.
5. Quality gates: CI builds, tests and validates Compose; scanning is added in CI.

AI-generated work is never accepted without compilation, tests, source inspection and adversarial evaluation.
