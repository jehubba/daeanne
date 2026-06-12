<!-- SPECKIT START -->

For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan at
specs/001-daeanne-frontend/plan.md

<!-- SPECKIT END -->

## Inter-Service Contracts

This monorepo contains multiple services that communicate over HTTP and Service Bus.
The contract between every producer and consumer is documented in `docs/contracts.md`.

**Read `docs/contracts.md` before modifying any code that crosses a service boundary.**

Rules:
1. When changing a request/response shape, serialization format, field name, field type,
   or envelope structure on **any** side, update `docs/contracts.md` and **all** consuming
   and producing code in the same commit.
2. Before creating new inter-service communication (new endpoints, new SB queues, new DTOs),
   add the contract to `docs/contracts.md` first, then implement both sides.
3. When you see a mismatch between `docs/contracts.md` and code, fix the code to match
   the contract (or update the contract if the code is intentionally ahead — but update
   both in the same commit).
4. Shared types in `Daeanne.Shared` are the source of truth for Service Bus message shapes.
   The contract doc describes them; the code defines them.
5. The Bridge relay layer (`FrontendRelayEndpoints.cs`) is the translation boundary between
   the Dispatcher's raw `AgentTask` shape and the Frontend's `TaskDto` shape.
   If either side changes, the relay mapping must be updated.
