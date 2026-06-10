# Daeanne Frontend — Spec

> **This is the spec.** It describes what the app is and why it exists, expressed as
> user stories, acceptance criteria, and design goals. It is implementation-agnostic.
> Two entirely different implementations (Blazor, React, native iOS) would each satisfy
> this spec while having completely different plans.
>
> See `plan.md` for the implementation choices for the current version.

---

## Problem Statement

Daeanne has no human-accessible interface. All interaction is via email and SMS.
This creates friction: checking task status requires an email exchange, there is no
ambient awareness of what's running, and reading a long research report in email is
poor UX. On mobile, composing commands is especially awkward.

A web frontend gives Jeffrey direct visibility and low-friction access from any device.

---

## Users

**One user: Jeffrey.**

This is a personal-use application. There is no multi-user scenario. Authentication
exists solely to prevent unauthorized access, not to differentiate user roles.

---

## User Stories

### Situational Awareness
- As Jeffrey, I want to see all running, pending, and recently completed tasks at a
  glance, so I know what Daeanne is doing without sending an email.
- As Jeffrey, I want to see whether any tasks are blocked or escalated, so I know
  when my input is needed.
- As Jeffrey, I want to see today's trend highlights surfaced automatically, so I
  get the signal without going looking for it.

### Command & Response
- As Jeffrey, I want to send a text command to Daeanne from my phone in a few
  seconds, as naturally as sending a text message.
- As Jeffrey, I want to see Daeanne's response in the same view, in readable form,
  without switching to email.

### Content Access
- As Jeffrey, I want to read the full output of a completed task (research report,
  daily summary, trend report) in the UI, so I don't have to find it in email.
- As Jeffrey, I want to see the most recent exchange history as context when
  composing a new command.

### Device & Availability
- As Jeffrey, I want to install the app to my phone home screen and have it behave
  like a native app (no browser chrome, offline graceful degradation).
- As Jeffrey, I want the interface to be usable on a phone screen without zooming
  or horizontal scrolling.

---

## Acceptance Criteria

### Core (must be true for v1)

- [ ] Can view a list of tasks by status (Running, Pending, Blocked, recent Succeeded/Failed)
- [ ] Each task shows: type, brief topic, status, age
- [ ] Can send a free-text command; receive and display Daeanne's response
- [ ] Can read the full output of any completed task (scrollable, readable on mobile)
- [ ] Works correctly on a 390px-wide viewport (iPhone SE and larger)
- [ ] Installable as a PWA home screen app on iOS and Android
- [ ] Requires authentication; only Jeffrey's identity can access any part of the app
- [ ] No local service port (47777 or any other) is exposed to the internet at any point

### Quality bar
- [ ] Task list refreshes without a full page reload
- [ ] Sending a command and receiving a response feels responsive (< 3s for ack, async for result)
- [ ] App is usable on a slow mobile connection (lazy loading, no multi-MB bundles)

---

## Design Goals

**Ambient awareness** — open the app, immediately understand what Daeanne is doing.
No digging required. Status should be the first thing visible.

**Low friction** — sending a command should feel like sending a text, not composing
an email. Single text input, send button, done.

**Mobile-first** — the primary access device is a phone. Desktop is a nice-to-have,
not the design target.

**No new local dependencies** — the local ecosystem (Dispatcher, Bridge, agents) is
unchanged. The cloud layer adds the UI; it does not alter local behavior.

**Secure by default** — the app is only as useful as its access to Daeanne's state.
That access must not be available to anyone other than Jeffrey.

---

## Non-Functional Requirements

| Concern | Requirement |
|---------|-------------|
| Authentication | Single-user; must use a durable identity (not just a shared secret) |
| Authorization | All API endpoints reject unauthenticated requests |
| Availability | Best-effort; offline graceful degradation acceptable |
| Performance | Task list load < 2s on LTE; command ack < 3s |
| Security | No local port exposure; all traffic cloud-to-local via pre-existing secure channel |
| Data residency | No new data stores; app reads from existing Dispatcher state |

---

## Out of Scope (any version)

- Multi-user access or sharing
- Admin control of the Dispatcher internals (start/stop agents, modify config)
- Workflow builder or agent configuration UI
- Music control
- Push notifications (may be a v2 concern; not required for v1)

---

## Tab Priorities

These are spec-level priorities — any implementation should deliver P0 before P1.

| Feature area | Priority | User story satisfied |
|--------------|----------|----------------------|
| Task status (list + detail) | P0 | Situational awareness |
| Chat / command input | P0 | Command & response |
| Task output viewer | P1 | Content access |
| Daily summary / trend highlights | P1 | Content access |
| Music | Deferred | Out of scope for v1 |

---

*Last updated: 2026-06-10*
