# Implement the human review queue with assignment and escalation

## Why

The system must never finalize consequential insurance decisions without an accountable human reviewer.

## Acceptance criteria

- [ ] A completed adjudication creates a pending ApprovalItem.
- [ ] Each item has priority, assignee role, SLA deadline, and history.
- [ ] Reviewers can approve, reject, edit, re-review, assign, escalate, and override priority when authorized.
- [ ] SLA worker reassigns stale work to a higher role.
- [ ] All state changes are auditable.
- [ ] State-machine tests cover allowed and blocked transitions.

## Pull request checklist

- Explain the assignment policy and escalation path.
- Attach test evidence.
- Add a self-review comment explaining why the model cannot finalize a decision.
