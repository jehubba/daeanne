# ADR-003: Bidirectional Service Bus Bridge as Separate Windows Service

**Date:** 2026-06-07
**Status:** Accepted

## Context

Inbound emails from Azure Communication Services (ACS) need to reach the
local Kestrel dispatcher. Outbound emails from Daeanne need to reach ACS.
We needed to decide how to implement this cloud/local boundary.

## Decision

**A single .NET Worker Service (`Daeanne.Bridge`) runs as a Windows Service
and acts as a bidirectional bridge between Azure Service Bus and local
Kestrel. It is a pure transport layer with no business logic.**

Flow:
- **Inbound:** Service Bus inbound queue → HTTP POST to Kestrel
- **Outbound:** HTTP from Kestrel outbound endpoint → Service Bus outbound queue
- **Cloud:** Azure Function receives outbound queue messages → sends via ACS

## Rationale

- Separating the Bridge from Kestrel means changing the cloud communication
  provider (e.g., replacing ACS/Service Bus with a different email pipeline)
  requires changes only to the Bridge and Azure Functions — not to Kestrel or
  any agent
- A Windows Service ensures the bridge starts on login and restarts on failure
- Service Bus provides durability: inbound emails queue in Azure if the local
  machine is offline and drain when the Bridge comes back online
- Two separate Service Bus queues (inbound, outbound) keep the flows
  independent and separately monitorable

## Consequences

- The Bridge is intentionally dumb — no routing logic, no business decisions
- Local components never call Azure Service Bus or ACS directly
- Bridge restart or downtime does not affect the Dispatcher or agents
  (tasks already in-flight continue; new inbound messages queue in Azure)
