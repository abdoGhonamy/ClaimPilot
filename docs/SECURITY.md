# Security

## Approval Authority Model

ClaimPilot gates the human review queue by **role-based monetary thresholds**
combined with **assignee-only authorization**. Every approval item is routed to
a role at creation (`AssignmentRouter`); `approve`/`reject`/`edit` may only be
performed by the item's assigned role, and `approve`/`edit` still require the
caller to stay within their monetary threshold. Every mutation of an approval
item is checked before it reaches the service layer; the API returns
standardized ProblemDetails for every denial.

### Roles and thresholds

| Role      | Approval authority (proposed amount) | May act on |
|-----------|--------------------------------------|------------|
| Adjuster  | up to $10,000                        | Approve/Reject/Edit on **Adjuster-assigned items only** up to threshold; Escalate; Re-review; View |
| Supervisor| up to $100,000                       | Approve/Reject/Edit on **Supervisor-assigned items only** up to threshold; Assign; Escalate; Re-review; View |
| Director  | unlimited (default $1,000,000,000)   | Approve/Reject/Edit on **Director-assigned items only**; Assign; priority override |
| Viewer    | none                                 | Queue and claim detail only (read-only) |

* Thresholds live in `appsettings.json` → `ApprovalAuthority` and are bound to
  `ApprovalAuthorityOptions` at startup. Neither thresholds nor the check are
  exposed to clients — the filter only answers with a 403 ProblemDetails.
* The amount check applies **only** to `Approve` and `Edit`, which move or
  change money. `Reject`, `Assign`, `Escalate` and `View` skip the monetary
  check (rejection spends nothing; assignment/escalation only redirects work).
* `AssigneeRole` is a typed enum (`Adjuster`, `Supervisor`, `Director`) stored
  as a capped string (`HasMaxLength(32)`), with `AssignedAt` recording when the
  item was (re)assigned. An item with no assignee (`NULL`) cannot be
  approved/rejected/edited — the API answers `409`.

### Enforcement points

* `[RequireApprovalAuthority(ApprovalAction.*)]` is a custom authorization
  filter (`ClaimPilot.API.Auth`) applied at the controller-action level on
  `approve`, `reject` and `edit`.
  * Unauthenticated callers get `401 Unauthorized`.
  * Callers without any role claim get `403 Forbidden`.
  * For `approve`/`reject`/`edit` the filter loads the item and enforces the
    **assignee-only rule**: an unassigned item is denied with `409 Conflict`;
    a caller whose roles do not include the item's `AssigneeRole` is denied
    with `403` and a `detail` of the form "This item is assigned to a {Role}…".
    **Higher roles cannot bypass the assignee rule** — a Supervisor may not
    approve an Adjuster-assigned item; it must be reassigned first.
  * With no amount check required (reject/assign/escalate/view), the filter
    passes once the route/id and (for approve/reject/edit) assignee checks pass.
  * Otherwise it resolves the `approvalItemId` route value, loads the approval
    item through `IApprovalQueueReader`, and compares the proposed amount to
    the caller's threshold via `IAuthorityService.CanApproveAsync`.
  * Every denial is logged (action, item id, roles/budget) at `Warning`.
* The controller guard stays `[Authorize(Roles = "Adjuster,Supervisor,Director")]`
  so the queue is never visible to Viewer. `assign` and `priority override`
  remain restricted to `Supervisor,Director`.

### Assignment routing

`AssignmentRouter.Compute(Priority, proposedAmount, IAuthorityService)` runs at
**item creation** so every pending item leaves the pipeline already assigned:

| Priority | Amount                                          | Assignee |
|----------|-------------------------------------------------|----------|
| Critical | any                                             | Director |
| High     | ≤ $100,000 / > $100,000                         | Supervisor / Director |
| Normal   | ≤ $10,000 / $10k–$100k / > $100k                | Adjuster / Supervisor / Director |
| Low      | same bands as Normal                            | Adjuster / Supervisor / Director |

The JSON API accepts `AssigneeRole` as a string (`"assignee": "Supervisor"`).

### Escalation routing and SLA reassignment

`approve/reject/edit` denials are resolved by escalation: `EscalateAsync`
marks the item `Escalated` and routes it to the **next higher level** — an
Adjuster escalates to a Supervisor, a Supervisor (or Director) to a Director —
and records the hand-off in the approval history. Escalation is open to
Adjusters, Supervisors and Directors, and only from `Pending`.

The `SlaEscalationWorker` additionally **reassigns** items that are stuck in
SLA and a single role cannot help:

| Rule | Condition                                        | Reassignment  |
|------|--------------------------------------------------|---------------|
| 1    | Adjuster-assigned, pending > 8h                  | → Supervisor  |
| 2    | Supervisor-assigned, pending > 16h               | → Director    |
| 3    | Unassigned, older than 2h                        | routed by priority (Critical → Director, High → Supervisor, else Adjuster) |

Each reassignment resets `AssignedAt` and is persistently audited.

### Demo credentials

Local-only demo users (see `Auth/SeedData.cs`):

| User        | Password                      | Roles                                   |
|-------------|-------------------------------|-----------------------------------------|
| `adjuster`  | `Adjuster#2026-local-only`    | Adjuster                                |
| `supervisor`| `Supervisor#2026-local-only`  | Adjuster, Supervisor                    |
| `director`  | `Director#2026-local-only`    | Adjuster, Supervisor, Director          |
| `viewer`    | `Viewer#2026-local-only`      | Viewer                                  |

> These credentials and the JWT signing key are development-only. Do not ship
> them; generate fresh secrets for any deployed environment.

### Testing the model

* `ReviewRoleGuardTests` — structural regression guard over the controller
  attributes (roles and filter placement).
* `Unit/AuthorityServiceTests` — threshold matrix for `CanApproveAsync`.
* `Unit/AssignmentRouterTests` — priority/amount routing matrix (11 cases).
* `Integration/ApprovalAuthorityFilterTests` — drives the filter directly and
  asserts 401/403/400/404, allow/deny by assignee role (including the
  higher-role-cannot-bypass case), the `409` for unassigned items, supervisor-only
  assigns, and the escalation hint on the 403 body.
* `ApprovalServiceStateMachineTests` — escalation routing (adjuster →
  supervisor, supervisor → director) and the Pending-only guard.