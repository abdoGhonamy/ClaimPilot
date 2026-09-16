# Improve security evidence and CI checks

## Why

The project needs automated safeguards and a documented mapping from threats to controls.

## Acceptance criteria

- [ ] Document OWASP web and LLM threats relevant to the system.
- [ ] Document current controls and explicit gaps.
- [ ] CI runs build, tests, Docker Compose validation, dependency audit, and secret-pattern scan.
- [ ] Repository includes contribution guidance, license, CODEOWNERS, and an issue template.
- [ ] No real secret or personal data is committed.

## Pull request checklist

- Include CI run evidence.
- Add a self-review comment on a control that still needs a production replacement.
