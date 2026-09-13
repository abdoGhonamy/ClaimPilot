# Security

## Approval Authority Model

ClaimPilot gates the human review queue by **role-based monetary thresholds**.
Every mutation of an approval item is checked before it reaches the service
layer; the API returns standardized ProblemDetails for every denial.

### Roles and thresholds

| Role      | Approval authority (proposed amount) | May act on |
|-----------|--------------------------------------|------------|
| Adjuster  | up to $10,000                        | Approve/Reject/Edit up to threshold; Escalate; Re-review; View |
| Supervisor| up to $100,000                       | Approve/Reject/Edit up to threshold; Assign; Escalate; Re-review; View |
| Director  | unlimited (default $1,000,000,000)   | Everything; Assign; priority override |
| Viewer    | none                                 | Queue and claim detail only (read-only) |

* Thresholds live in `appsettings.json` → `ApprovalAuthority` and are bound to
  `ApprovalAuthorityOptions` at startup. Neither thresholds nor the check are
  exposed to clients — the filter only answers with a 403 ProblemDetails.
* The amount check applies **only** to `Approve` and `Edit`, which move or
  change money. `Reject`, `Assign`, `Escalate` and `View` skip the monetary
  check (rejection spends nothing; assignment/escalation only redirects work).

### Enforcement points

* `[RequireApprovalAuthority(ApprovalAction.*)]` is a custom authorization
  filter (`ClaimPilot.API.Auth`) applied at the controller-action level on
  `approve`, `reject` and `edit`.
  * Unauthenticated callers get `401 Unauthorized`.
  * Callers without a working role get `403 Forbidden`.
  * With no amount check required (reject/assign/escalate/view), the filter
    passes immediately.
  * Otherwise it resolves the `approvalItemId` route value, loads the approval
    item through `IApprovalQueueReader`, and compares the proposed amount to
    the caller's threshold via `IAuthorityService.CanApproveAsync`.
  * Denials return `403` with a `ProblemDetails` body whose `detail` explains
    the caller's budget vs. the item amount and reminds them to escalate.
* The controller guard stays `[Authorize(Roles = "Adjuster,Supervisor,Director")]`
  so the queue is never visible to Viewer. `assign` and `priority override`
  remain restricted to `Supervisor,Director`.

### Escalation routing

`approve/reject/edit` denials are resolved by escalation: `EscalateAsync`
marks the item `Escalated` and routes it to the **next higher level** — an
Adjuster escalates to a Supervisor, a Supervisor (or the SLA worker) to a
Director — and records the hand-off in the approval history. Escalation is
open to Adjusters, Supervisors and Directors, and only from `Pending`.

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
* `Integration/ApprovalAuthorityFilterTests` — drives the filter directly and
  asserts 401/403/400/404, allow/deny by role, and the escalation hint on the
  403 body.
* `ApprovalServiceStateMachineTests` — escalation routing (adjuster →
  supervisor, supervisor → director) and the Pending-only guard.