# Add a runnable RAG evaluation harness

## Why

The project needs repeatable evidence for answer correctness, safe refusals, citations, version traps, and prompt-injection resistance.

## Acceptance criteria

- [ ] Run all 26 cases from `EvaluationCorpus` against the live API.
- [ ] Verify expected answer text, refusal correctness, and citation presence.
- [ ] Emit a timestamped JSON report.
- [ ] Return a non-zero exit code when a case fails.
- [ ] Support running a category such as `PromptInjection` independently.
- [ ] Document the command and record actual baseline results.

## Pull request checklist

- Include a sample report or summarized real result.
- Add a self-review comment describing the metric limitation: citation presence is not citation relevance.
