## Type
infrastructure

## Summary
Changes to local/CI/Docker tooling (compose, images, workflows, secrets handling).

## Changes
- Dockerfile / docker-compose / images
- CI workflow steps
- Secrets & env-var handling (never commit secrets)

## Verification
- [ ] `docker compose config -q` passes
- [ ] Container stack boots and /health is green
- [ ] Seeding + adjudication smoke test in containers (if touched)

## Rollout
Steps to redeploy (pull, recreate services).

## Related
#<!-- issue -->