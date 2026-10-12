# Security policy

How to report a vulnerability in Marshal and which versions receive fixes.

## Reporting a vulnerability

Do not open a public issue for a security problem.

Report it privately. Contact the maintainer through the profile of the repository owner on the repository host and ask for a private channel before you share details.

Include the affected version or commit, the steps to reproduce, and the impact you expect. Do not include real secrets or personal data.

## Supported versions

Marshal has no tagged release yet. Once releases exist, only the latest release receives security fixes. Until then, only the current `master` branch is supported.

## Threat model in short

Marshal acts on a home with an LLM, so the design assumes the model can be manipulated and limits what it can do.

- **Content from mail, RSS, notes and web pages is untrusted data.** A run that read it is tainted, and memory and task writes in that run need confirmation.
- **The model only sees tools it may use.** The guard enforces the same rules server-side as a second line.
- **Risky actions are frozen and confirmed by you.** Tool output never counts as confirmation.
- **Mail has no send path** and the model gets no raw G-code or free-form device commands.
- **`/config` holds no secrets.** Secret values come from the secret store, and startup fails on literal secrets.
- **Every user-owned row has an owner,** and data access is owner-scoped.

The full model is described in [the security model](docs/concepts/security-model.md).

## Out of scope

Marshal is built for a trusted home network behind your own reverse proxy. Reports that require an attacker who already controls the host, the `/config` volume or the secret store are out of scope.
