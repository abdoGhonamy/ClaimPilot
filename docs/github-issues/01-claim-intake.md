# Add secure claim intake and supporting-document upload

## Why

Adjusters need a protected way to create a claim and attach evidence before adjudication.

## Acceptance criteria

- [ ] Adjuster and Supervisor can create claims; Viewer cannot.
- [ ] Policy number, incident date, amount, and description are validated server-side.
- [ ] Supporting-document upload validates size, extension, and file signature.
- [ ] Uploaded files use generated storage names outside `wwwroot`.
- [ ] Claim creation and upload create audit records.
- [ ] Unit and integration tests cover valid and rejected requests.

## Pull request checklist

- Describe the API endpoints and validation rules.
- Include the test command and result.
- Add at least one self-review comment on a security-sensitive line.
