# Enforce server-side approval authority thresholds

## Why

The UI cannot be the security boundary for insurance approval decisions.

## Acceptance criteria

- [ ] Adjuster, Supervisor, Director, and Viewer have distinct permissions.
- [ ] Approve, reject, and edit actions enforce assignee-role authorization on the server.
- [ ] Approve and edit actions enforce monetary authority thresholds.
- [ ] Unassigned items return a clear conflict response.
- [ ] Higher roles do not bypass an assigned lower role without reassignment.
- [ ] Tests cover 401, 403, 409, allowed paths, and threshold boundaries.

## Pull request checklist

- Link this issue using `Closes #<number>`.
- Add a self-review comment on the authorization filter.
