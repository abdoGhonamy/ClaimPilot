## Title
A short, imperative summary of the change.

## Type
Pick ONE template for the targeted description:
- [bug-fix](pull_request_template/bug-fix.md)
- [feature](pull_request_template/feature.md)
- [chore](pull_request_template/chore.md)
- [infrastructure](pull_request_template/infrastructure.md)
- [docs](pull_request_template/docs.md)
- [tests](pull_request_template/tests.md)
- [release](pull_request_template/release.md)
- [hotfix](pull_request_template/hotfix.md)

## Summary
What and why, in a few sentences.

## Verification
- [ ] `dotnet build ClaimPilot.slnx` clean
- [ ] `dotnet test` green
- [ ] E2E / manual verification (if behavior changed)

## Checks
- No secrets or local credentials committed.
- Decision amounts come from the deterministic engine, not the LLM.
- Refusals use the exact string
  `Not enough information in the policy corpus to determine this.`

## Related
Closes #<!-- issue -->