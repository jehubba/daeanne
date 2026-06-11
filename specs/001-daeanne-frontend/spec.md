# Feature Specification: Daeanne Frontend Web App

**Feature Branch**: `feat/001-daeanne-frontend`

**Created**: 2026-06-11

**Status**: Draft

**Input**: User description: "Reference the doc at docs/frontend/spec.md — this is for the app in the folder Daeanne.Frontend"

## Clarifications

### Session 2026-06-11

- Q: How should Jeffrey navigate between the task dashboard, chat interface, and task detail views? → A: Bottom tab bar (Tasks, Chat) with task detail as a drill-down overlay
- Q: How many completed tasks should appear in the "recent" completed list? → A: Completed within last 24 hours
- Q: What refresh mechanism should the task list use? → A: Timed polling every 30 seconds plus manual pull-to-refresh gesture

## User Scenarios & Testing _(mandatory)_

### User Story 1 - Task Status Dashboard (Priority: P1)

Jeffrey opens the Daeanne frontend on his phone and immediately sees the Tasks tab — a list of all tasks grouped by status: Running, Pending, Blocked, and completed within the last 24 hours (Succeeded/Failed). Each task shows its type, a brief topic, current status, and age. The list refreshes without requiring a full page reload. A bottom tab bar provides one-tap navigation between the Tasks and Chat views.

**Why this priority**: This is the core value proposition — ambient awareness of what Daeanne is doing without sending an email. It is the first thing visible on launch and the most frequently used view.

**Independent Test**: Can be fully tested by opening the app and verifying the task list renders with correct groupings and data. Delivers immediate situational awareness value even without chat or content features.

**Acceptance Scenarios**:

1. **Given** Jeffrey is authenticated, **When** he opens the app, **Then** he sees a list of tasks grouped by status (Running, Pending, Blocked, Succeeded/Failed completed within last 24 hours)
2. **Given** the task list is displayed, **When** 30 seconds elapse or Jeffrey performs a pull-to-refresh gesture, **Then** the list updates without a full page reload
3. **Given** each task entry, **When** Jeffrey reads it, **Then** it shows the task type, brief topic, status, and age
4. **Given** no tasks exist, **When** Jeffrey opens the app, **Then** he sees a clear empty state message

---

### User Story 2 - Send Commands via Chat (Priority: P1)

Jeffrey taps the Chat tab and types a free-text command into a simple text input at the bottom of the screen and taps send. The experience feels like sending a text message — minimal friction, no email composition overhead. He receives an acknowledgment quickly, and Daeanne's full response appears in the same view once processing completes.

**Why this priority**: Command input is co-equal with task status as the core interaction. Without it, the app is read-only and Jeffrey still needs email/SMS to interact with Daeanne.

**Independent Test**: Can be tested by typing a command, verifying the acknowledgment appears promptly, and confirming the response renders in the same conversational view once ready.

**Acceptance Scenarios**:

1. **Given** Jeffrey is authenticated and on the chat view, **When** he types a command and taps send, **Then** an acknowledgment appears within 3 seconds
2. **Given** a command has been sent, **When** Daeanne finishes processing, **Then** the response appears in the same conversational view in readable form
3. **Given** the chat view, **When** Jeffrey reviews it, **Then** he sees recent exchange history as context for composing new commands
4. **Given** a command submission fails, **When** the error occurs, **Then** Jeffrey sees a clear error message and the input text is preserved

---

### User Story 3 - Read Full Task Output (Priority: P2)

Jeffrey taps on a completed task to view its full output — a research report, daily summary, or trend report. The content renders in a scrollable, readable format on a mobile screen without horizontal scrolling or zooming.

**Why this priority**: Content access is the second tier of value. Jeffrey can see _that_ tasks completed (P1) before needing to read _what_ they produced. This story builds on the task list.

**Independent Test**: Can be tested by selecting a completed task and verifying the output renders correctly and is fully scrollable on a mobile viewport.

**Acceptance Scenarios**:

1. **Given** a completed task in the list, **When** Jeffrey taps it, **Then** the full task output displays in a scrollable, readable view
2. **Given** the output view on a 390px-wide viewport, **When** Jeffrey reads the content, **Then** no horizontal scrolling or zooming is required
3. **Given** a long output (research report), **When** Jeffrey scrolls, **Then** the content loads progressively without blocking the UI

---

### User Story 4 - Trend Highlights (Priority: P2)

Jeffrey sees today's trend highlights surfaced automatically on the dashboard, giving him the signal without going looking for it.

**Why this priority**: Trend highlights add ambient intelligence — Jeffrey gets value passively. This builds on the dashboard (P1) by enriching it with summarized content.

**Independent Test**: Can be tested by verifying that the dashboard shows today's trend highlights when trend data is available, and shows a graceful empty state when no trend data exists.

**Acceptance Scenarios**:

1. **Given** trend data is available for today, **When** Jeffrey opens the app, **Then** trend highlights are visible on the dashboard
2. **Given** no trend data exists for today, **When** Jeffrey opens the app, **Then** the trend section shows a graceful empty state or is hidden

---

### User Story 5 - PWA Installation and Mobile Experience (Priority: P1)

Jeffrey installs the app to his phone home screen. It launches without browser chrome, behaves like a native app, and is fully usable on a phone screen (390px and wider) without zooming or horizontal scrolling. On a slow mobile connection, the app still loads and remains functional.

**Why this priority**: The spec requires mobile-first design and PWA installability as core properties, not enhancements. Without this, the app fails its primary access scenario.

**Independent Test**: Can be tested by installing the PWA on iOS/Android, launching from the home screen, and verifying native-like behavior, responsive layout, and acceptable load times on a throttled connection.

**Acceptance Scenarios**:

1. **Given** Jeffrey visits the app in a mobile browser, **When** he adds it to his home screen, **Then** it installs as a PWA and launches without browser chrome
2. **Given** a 390px-wide viewport (iPhone SE), **When** Jeffrey uses any feature, **Then** the UI is fully functional without zooming or horizontal scrolling
3. **Given** a slow mobile connection (3G-equivalent), **When** Jeffrey loads the app, **Then** the task list appears within a reasonable time with lazy-loaded content

---

### User Story 6 - Authentication and Security (Priority: P1)

The app requires authentication before any content or functionality is accessible. Only Jeffrey's identity can access the app. No local service port is exposed to the internet at any point.

**Why this priority**: Security is a non-negotiable gating requirement. The app accesses Daeanne's task state and command interface — unauthorized access must be prevented.

**Independent Test**: Can be tested by attempting unauthenticated access (verify redirect to login), authenticating as Jeffrey (verify access), and attempting access with a different identity (verify rejection).

**Acceptance Scenarios**:

1. **Given** an unauthenticated user, **When** they access any route, **Then** they are redirected to the login page
2. **Given** Jeffrey authenticates with his identity, **When** login completes, **Then** he has full access to the app
3. **Given** a different authenticated user, **When** they attempt to access the app, **Then** they are denied access
4. **Given** the deployed app, **When** inspecting network configuration, **Then** no local service port (47777 or any other) is exposed to the internet

---

### Edge Cases

- What happens when the Dispatcher is offline? The app should show a clear connectivity status and degrade gracefully (show last-known state or a "Daeanne is offline" indicator).
- What happens when a command is sent but the response never arrives? A timeout mechanism should inform Jeffrey that the request may not have been processed, with an option to retry.
- What happens when the task list contains hundreds of items? The list should paginate or virtualize to maintain performance.
- What happens when Jeffrey loses network connectivity mid-session? The PWA should show an offline indicator and preserve any unsent command text.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: System MUST display a list of tasks grouped by status (Running, Pending, Blocked, Succeeded/Failed completed within last 24 hours)
- **FR-002**: Each task entry MUST show task type, brief topic, current status, and age
- **FR-003**: The task list MUST refresh automatically via polling every 30 seconds without requiring a full page reload
- **FR-004**: Users MUST be able to send free-text commands through a chat-style interface
- **FR-005**: System MUST display an acknowledgment within 3 seconds of command submission
- **FR-006**: System MUST display Daeanne's full response in the same conversational view once processing completes
- **FR-007**: System MUST display recent exchange history as context when composing new commands
- **FR-008**: Users MUST be able to view the full output of any completed task in a scrollable, readable format
- **FR-009**: System MUST be installable as a PWA on iOS and Android home screens
- **FR-010**: System MUST function correctly on viewports 390px wide and larger without horizontal scrolling or zooming
- **FR-011**: System MUST require authentication for all routes and API endpoints
- **FR-012**: System MUST restrict access to only Jeffrey's verified identity
- **FR-013**: System MUST NOT expose any local service ports to the internet
- **FR-014**: System MUST display today's trend highlights automatically on the dashboard when available
- **FR-015**: System MUST show clear status indicators when the backend is unreachable (Dispatcher offline)
- **FR-016**: System MUST preserve unsent command text when network connectivity is lost
- **FR-017**: System MUST provide a bottom tab bar for one-tap navigation between Tasks and Chat views; task detail MUST open as a drill-down overlay from the task list
- **FR-018**: System MUST support a manual pull-to-refresh gesture on the task list to trigger an immediate refresh

### Key Entities

- **Task**: Represents a unit of work managed by the Dispatcher. Key attributes: type, topic, status (Running, Pending, Blocked, Succeeded, Failed), age, completion time, full output content. Completed tasks are shown if completed within the last 24 hours.
- **Command**: A free-text instruction sent by Jeffrey to Daeanne. Key attributes: prompt text, correlation identifier, submission time.
- **Response**: Daeanne's reply to a command. Key attributes: correlation identifier, success/failure status, response content.
- **Trend Highlight**: A summarized trend signal surfaced from Daeanne's trend analysis. Key attributes: date, summary content.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: Jeffrey can assess the status of all active tasks within 5 seconds of opening the app
- **SC-002**: Sending a command takes fewer than 3 taps/actions (open app → type → send)
- **SC-003**: Command acknowledgment appears within 3 seconds of submission
- **SC-004**: Task list loads within 2 seconds on an LTE mobile connection
- **SC-005**: Full task output is readable on a 390px-wide screen without horizontal scrolling
- **SC-006**: The app is installable as a home screen PWA on iOS Safari and Android Chrome
- **SC-007**: Zero unauthorized access incidents — only Jeffrey's identity can access any content
- **SC-008**: The app remains usable (shows last-known state or clear offline indicator) when the backend is temporarily unreachable
- **SC-009**: Task list refreshes reflect status changes without requiring manual page reload

## Assumptions

- Jeffrey is the sole user; no multi-user, role-based, or sharing functionality is needed
- The existing local task management service provides all task state — no new data stores are introduced
- Connectivity between the cloud-hosted frontend and the local services flows through an existing secure messaging channel — no direct port exposure or tunneling
- The existing messaging relay service already bridges cloud and local services and can be extended for frontend-specific message paths
- Authentication uses Jeffrey's existing organizational identity — no new identity registration is required for the user
- The app targets modern mobile browsers (iOS Safari 15+, Chrome for Android) — legacy browser support is not required
- Offline graceful degradation means showing last-known state or an offline indicator, not full offline functionality
- Performance targets (2s load, 3s ack) assume standard LTE mobile connectivity, not edge-case network conditions
- Push notifications are out of scope for this version; polling or manual refresh is acceptable
