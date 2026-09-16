# Submission Checklist

## Repository evidence completed locally

- [x] Domain workflow, human review queue, Docker Compose, migrations and synthetic corpus
- [x] Evaluation harness, required reports, ADRs, agentic workflow log and teaching pack
- [x] Postman demo collection and minimal browser UI
- [x] CI build/test, Compose validation, dependency audit and history secret-pattern scan

## Actions requiring your GitHub account or recording equipment

- [ ] Confirm the invitation assignment is D2T5, or add National-ID derivation to README.
- [ ] Push to a public GitHub repository and keep it public for 30 days.
- [ ] Create/link at least eight real pull requests with descriptions, linked issues and self-review comments.
- [ ] Enable branch protection on `main`, required CI checks, Dependabot and GitHub code scanning.
- [ ] Create a board or milestone showing implemented and deferred work.
- [ ] Record an unlisted 5–8 minute product demo: ingest, cited answer, refusal, live run, approval, trace and review queue.
- [ ] Record an unlisted 10-minute face-and-voice teaching sample using `teaching/ClaimPilot-Trustworthy-RAG.pptx`.
- [ ] Put both verified unlisted links in README.
- [ ] Clone fresh on another machine and verify `docker compose up -d --build`.
- [ ] Run a full-history secret scan and record the result; confirm CI is green on main.

## Product demo script

1. Open the browser UI at `/` or import the Postman collection.
2. Sign in as adjuster and load `CLAIM-2023-001`.
3. Ask a supported policy question, then an unsupported question to show refusal.
4. Run adjudication and show SSE events and the run trace.
5. Sign in as supervisor, show queue assignment, statistics and audit history.
