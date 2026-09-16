# ClaimPilot Postman video demo

Import `ClaimPilot-Demo.postman_collection.json` and, optionally,
`ClaimPilot-Local.postman_environment.json` into Postman. Start the API first:

```bash
docker compose up -d --build
```

Use `http://localhost:8080` for `baseUrl` with Docker Compose, or leave the default
`http://localhost:5028` for the local `dotnet run` profile. Run the collection from top to
bottom in the Collection Runner; the green test results make the walkthrough easy to show.

Suggested 3-minute recording order:

1. Show **Service health**, then **Login as adjuster** and **Confirm current adjuster**.
2. Run **List seeded claims** and **Show version-trap claim details**. Explain that the
   `CLAIM-2023-001` incident is deliberately priced against the historical AUT-2022 wording.
3. Run **Create a claim** to demonstrate protected intake and automatic test assertions.
4. Run **Ask a grounded policy question** and highlight the answer, refusal flag, and citations.
5. Run **Stream adjudication (SSE)**. Keep the response pane open to show agent events and
   `run_complete`; this is the strongest visual proof of the orchestration workflow.
6. Run **Login as supervisor**, **Review queue**, **Review statistics**, and **Audit trail** to
   close with human approval controls and traceability.

The stream creates a review item and changes demo data, so do not run an approval request
against it unless you intend to finalize that decision. The collection intentionally stops at
the queue and audit views to keep repeat recordings safe.
