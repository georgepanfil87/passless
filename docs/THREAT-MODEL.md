# Threat model

> Placeholder. Written alongside the implementation, not retrofitted after it.

## Assets

## Trust boundaries

## Adversaries

## Threats and mitigations

### Credential replay

### Phishing and origin confusion

### Refresh token theft

### Authenticator cloning

### Enumeration of accounts and credentials

### Denial of service

## Accepted risks

### IP addresses are stored, and they are personal data

Every session row and every audit row carries the client address, as `inet`.
This is deliberate — a user cannot recognise a session they did not start
without some indication of where it came from, and an audit trail that cannot
say where a critical event originated is close to useless during an incident.

It is worth being precise about what that means rather than letting the
"coarse location only" rule imply we store less than we do:

- The **address itself** is stored, in full, on both `sessions` and
  `audit_events`. That is the sensitive value.
- The **location** shown in the session list is derived from it at read time and
  never persisted. It is city and country at most; the resolver's return type
  has no room for coordinates.
- Audit rows are append-only by database trigger, so addresses in them **cannot
  be redacted or deleted** once written. Any retention policy has to be
  implemented as a partition drop or a table rotation, not an `UPDATE`.

Outstanding: this project has no retention schedule. A real deployment needs
one, and needs to decide whether audit addresses should be truncated to a /24
(IPv4) or /48 (IPv6) prefix on write — which keeps them useful for spotting a
change of network while storing materially less about the person.

### A revoked session's access token can outlive the revocation

Bounded by the access-token lifetime, and only when the revocation cache is
unreachable. See the README section on revocation windows for the exact
guarantee and why failing open was chosen.

## Explicitly out of scope
