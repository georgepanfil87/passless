# 1. Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-08-26

## Context

This project exists to be read. Most of its interesting content is not the code
but the reasoning behind it: why a challenge is stored server-side rather than
signed into a stateless token, why a signature counter of zero is tolerated, why
a reused refresh token revokes a family instead of a single token.

That reasoning has a short half-life. Six months on, a decision with a good
reason and a decision made by accident look identical in the source tree, and
the only honest way to tell them apart is to have written the reason down at the
time.

## Decision

Decisions that constrain future work, or that a reviewer would reasonably
challenge, are recorded here as numbered Architecture Decision Records in the
style described by Michael Nygard. One file per decision, numbered sequentially,
never edited after acceptance -- superseded records get a new ADR that points
back at the old one.

Decisions with no consequences beyond the current pull request do not get an
ADR. Not everything is architecture.

## Consequences

Adding a security-relevant decision means writing prose as well as code, which
is slower. The intended payoff is that the threat model and the design document
stay derived from real decisions rather than being reconstructed afterwards from
whatever the code happens to do.
