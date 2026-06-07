# ADR-001: Monorepo for Infrastructure and Docs

**Date:** 2026-06-07
**Status:** Accepted

## Context

The Daeanne system consists of multiple components: a local HTTP dispatcher
(Kestrel), a Service Bus bridge, agent profiles, and architecture
documentation. We needed to decide how to organize these across repos.

Options considered:
1. Everything in one repo
2. Everything in separate repos
3. Hub monorepo for infrastructure + docs; separate repos for reusable agents

## Decision

**Option 3: Hub monorepo (`daeanne`) for infrastructure, documentation, and
Daeanne's own agent profile. Separate repos for specialized agents that have
an independent lifecycle (e.g., `research-agent`).**

## Rationale

- A solo developer benefits from one place to reason over the whole system
- .NET infrastructure and agent markdown files have very different change
  cadences; they coexist fine in one repo with clear directory boundaries
- The `research-agent` repo was already established and working with its own
  symlink setup — moving it adds disruption with no clear gain
- Future general-purpose agents (reusable outside Daeanne) should live in
  their own repos; Daeanne-specific agents live in `daeanne/agents/`
- Architecture docs belong in the hub where they're always co-located with
  the infrastructure they describe

## Consequences

- The `daeanne` repo is the single source of truth for system architecture
- `research-agent` and any future general-purpose agent repos are
  independent — the daeanne setup script notes they require separate setup
- New sub-agents that are only used by Daeanne go in `daeanne/agents/`
