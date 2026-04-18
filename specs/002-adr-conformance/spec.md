# Feature Specification: Device API Architecture Conformance

**Feature Branch**: `002-adr-conformance`  
**Created**: 2026-04-05  
**Status**: Draft  
**Input**: User description: "lets validate the recently added architecture design records in the @docs/adr folder by implementing changes to the device management api so the code can conform to the architecture"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Invalid Device Data is Rejected at the Domain Layer (Priority: P1)

As a developer integrating with the device management API, when I submit a device registration request with invalid data (blank name, malformed IP address, out-of-range port), I want to receive clear, structured validation error messages without causing any exceptions or inconsistent system state.

**Why this priority**: Domain-layer validation is the foundation of correctness. If invalid data can reach the database or downstream services, it creates compounding errors. All other improvements depend on the domain reliably protecting its own invariants.

**Independent Test**: Can be fully tested by submitting malformed device payloads to the create and update endpoints and verifying that structured error messages are returned and no data is persisted.

**Acceptance Scenarios**:

1. **Given** a request to register a new device, **When** the IP address field contains a non-IP string (e.g., "not-an-ip"), **Then** the API returns a 422 response with a field-level error identifying the IP address as invalid.
2. **Given** a request to register a new device, **When** the HTTP port is outside the valid range (e.g., 0 or 70000), **Then** the API returns a 422 response with a field-level error identifying the port as out of range.
3. **Given** a request to register a new device, **When** the device name is blank or whitespace-only, **Then** the API returns a 422 response with a field-level error identifying the name as required.
4. **Given** a request to register a new device, **When** all fields are valid, **Then** the device is created and a 201 response is returned with the device details (password excluded).

---

### User Story 2 - Each Device Operation is Independently Maintainable (Priority: P2)

As a developer maintaining the device management API, I want each device operation (create, read, update, delete) to be isolated in its own module so that I can modify, test, or extend any single operation without risk of breaking the others.

**Why this priority**: Operational isolation directly reduces the cost and risk of maintenance. It enables individual operations to be developed and reviewed independently, and allows new team members to understand the system one operation at a time.

**Independent Test**: Can be fully tested by verifying that each device operation (create, read-all, read-one, update, delete) continues to work correctly after the refactoring, using the same API contract as before.

**Acceptance Scenarios**:

1. **Given** a running device management API, **When** I call the create-device endpoint with valid data, **Then** the device is created and returned with a 201 status.
2. **Given** existing devices in the system, **When** I call the list-devices endpoint, **Then** all devices are returned with a 200 status.
3. **Given** an existing device, **When** I call the get-device endpoint with its ID, **Then** that device is returned with a 200 status.
4. **Given** an existing device, **When** I call the update-device endpoint with valid data, **Then** the device is updated and returned with a 200 status.
5. **Given** an existing device, **When** I call the delete-device endpoint with its ID, **Then** the device is removed and a 204 status is returned.

---

### User Story 3 - Duplicate Device Registrations are Prevented (Priority: P3)

As a system administrator managing Hikvision devices, I want the system to prevent duplicate registrations for the same physical device so that configuration data remains trustworthy and no device appears multiple times in the system.

**Why this priority**: Duplicate prevention is an existing capability that must continue to work correctly after the architecture changes. It is lower priority than establishing correct validation and structure, but it is a visible data quality guarantee to operators.

**Independent Test**: Can be fully tested by attempting to register two devices with the same IP address and port combination and verifying a conflict error is returned for the second attempt.

**Acceptance Scenarios**:

1. **Given** a device already registered with a specific IP address and HTTP port, **When** I submit a new registration request with the same IP address and HTTP port, **Then** the API returns a 409 Conflict response and no duplicate record is created.

---

### Edge Cases

- What happens when a device update request sets the IP address to one already used by a different device?
- How does the system handle a delete request for a device ID that does not exist?
- What happens when a get request is made for a non-existent device ID?
- How are password values handled when a device is updated — is the existing encrypted password preserved if no new password is supplied?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST reject device creation and update requests where the name is blank or whitespace-only, returning a structured error identifying the field.
- **FR-002**: The system MUST reject device creation and update requests where the IP address is not a valid IPv4 or IPv6 address, returning a structured error identifying the field.
- **FR-003**: The system MUST reject device creation and update requests where the HTTP port is outside the range 1–65535, returning a structured error identifying the field.
- **FR-004**: The system MUST prevent registration of two devices with the same IP address and HTTP port combination, returning a conflict error on the second attempt.
- **FR-005**: The system MUST never return a device password in any API response.
- **FR-006**: The system MUST store device passwords in an encrypted form such that they can be retrieved for future device communication.
- **FR-007**: Each device operation (create, list, get, update, delete) MUST be independently deployable and testable as an isolated unit.
- **FR-008**: Each device operation MUST register its own dependencies and routing, without relying on a central configuration file for those concerns.
- **FR-009**: Validation errors MUST be expressed as structured data (field name + message) rather than unstructured strings or exceptions.
- **FR-010**: The system MUST continue to support all existing device management operations (create, list, get by ID, update, delete) with the same HTTP semantics after the architecture changes.

### Key Entities

- **Device**: A physical Hikvision camera or NVR being managed. Identified by a unique combination of IP address and HTTP port. Has a name, optional hostname, credentials (username and password), and a system-generated ID. The password is never visible in API responses.
- **IP Address**: A typed representation of a valid network address (IPv4 or IPv6). Cannot be an arbitrary string.
- **Port**: A typed representation of a valid TCP port number (1–65535). Cannot be zero or exceed 65535.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of existing integration tests pass after the architecture refactoring with no changes to test assertions.
- **SC-002**: Each device operation (create, list, get, update, delete) can be located, understood, and modified in isolation within 5 minutes by a developer unfamiliar with the codebase.
- **SC-003**: All 5 validation rules (name required, valid IP, valid port range, no duplicate IP+port, password never returned) are enforced and covered by automated tests.
- **SC-004**: No device with invalid name, IP address, or port can be persisted to the database — verified by attempting each invalid case through the API.

## Assumptions

- The external API contract (HTTP methods, URL paths, request/response shapes) remains unchanged — this is an internal refactoring.
- The existing integration test suite is the primary regression safety net; no new test infrastructure is needed.
- Both ADRs (vertical slice architecture and domain model validation) are applied together in this single change, as they are tightly related and the codebase is small enough to refactor atomically.
- The update operation requires all fields to be supplied (no partial/PATCH semantics) — consistent with the current implementation.
- Password update behaviour: if a password is included in an update request, it replaces the stored encrypted password; the spec does not require a "keep existing password" option.
