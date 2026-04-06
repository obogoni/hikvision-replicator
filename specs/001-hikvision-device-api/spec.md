# Feature Specification: Hikvision Device Management API

**Feature Branch**: `001-hikvision-device-api`  
**Created**: 2026-04-05  
**Status**: Draft  
**Input**: User description: "Create a API using .NET 10 to manage hikvision face recognition terminals, the API should expose methods to create, update, get and delete devices. each device has a name, ip address, http port number, username and password"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register a New Device (Priority: P1)

An operator needs to add a new Hikvision face recognition terminal to the system so it can be tracked and managed centrally. They provide the device's identifying information — name, IP address, HTTP port, and credentials — and the system persists it for future use.

**Why this priority**: Without the ability to register devices, no other operation is possible. This is the foundational capability of the entire feature.

**Independent Test**: Can be fully tested by submitting a valid device registration request and verifying the device appears in subsequent lookups, delivering a working device registry.

**Acceptance Scenarios**:

1. **Given** no device exists with the provided IP address and port, **When** an operator submits a registration request with all required fields (name, IP address, port, username, password), **Then** the system creates the device record and returns the newly created device with a unique identifier (password excluded from response).
2. **Given** a device already exists with the same IP address and port combination, **When** an operator submits a registration request with those same values, **Then** the system rejects the request with a clear conflict error message.
3. **Given** an operator submits a registration request with a missing required field, **When** the system validates the input, **Then** the system rejects the request and identifies the specific missing field(s).
4. **Given** an operator submits a registration request with an invalid IP address format, **When** the system validates the input, **Then** the system rejects the request with a descriptive validation error.
5. **Given** an operator submits a registration request with a port number outside the valid range (1–65535), **When** the system validates the input, **Then** the system rejects the request with a descriptive validation error.

---

### User Story 2 - Retrieve Device Information (Priority: P2)

An operator needs to view details of one or all registered devices so they can inspect configuration, verify connectivity parameters, or audit the device inventory.

**Why this priority**: Retrieval is required before update or delete operations can be performed meaningfully. It also enables basic inventory visibility, which is core to device management.

**Independent Test**: Can be fully tested by registering a device and then retrieving it by ID and via the full list, confirming accurate data is returned (with password omitted).

**Acceptance Scenarios**:

1. **Given** one or more devices are registered, **When** an operator requests the list of all devices, **Then** the system returns all registered devices with their details (password excluded from all entries).
2. **Given** a device exists with a known identifier, **When** an operator requests that specific device by identifier, **Then** the system returns the device's details (password excluded).
3. **Given** no device exists with the requested identifier, **When** an operator requests that device, **Then** the system returns a clear not-found error message.
4. **Given** no devices have been registered, **When** an operator requests the list of all devices, **Then** the system returns an empty list (not an error).

---

### User Story 3 - Update Device Information (Priority: P3)

An operator needs to update the configuration of an existing device — for example, when the device's IP address changes, credentials are rotated, or the device is renamed — without having to delete and re-register it.

**Why this priority**: Devices in production environments may change IP address, port, or credentials over time. Updating avoids data loss from re-registration.

**Independent Test**: Can be fully tested by registering a device, updating one or more fields, and verifying the changes are reflected on subsequent retrieval.

**Acceptance Scenarios**:

1. **Given** a device exists with a known identifier, **When** an operator submits an update with new valid values for one or more fields, **Then** the system updates only the provided fields and returns the updated device (password excluded).
2. **Given** an operator submits an update that would cause an IP address and port conflict with another existing device, **When** the system validates the input, **Then** the system rejects the update with a conflict error.
3. **Given** an operator submits an update with invalid field values (e.g., malformed IP, out-of-range port), **When** the system validates the input, **Then** the system rejects the update with descriptive validation errors.
4. **Given** no device exists with the requested identifier, **When** an operator submits an update, **Then** the system returns a clear not-found error.

---

### User Story 4 - Delete a Device (Priority: P4)

An operator needs to remove a device from the system when it is decommissioned or replaced, so the inventory remains accurate.

**Why this priority**: Device removal is important for inventory hygiene but is lower priority than registration and retrieval, which are needed first to operate the system at all.

**Independent Test**: Can be fully tested by registering a device, deleting it by identifier, and confirming it no longer appears in the device list.

**Acceptance Scenarios**:

1. **Given** a device exists with a known identifier, **When** an operator requests deletion of that device, **Then** the system removes the device and confirms the deletion.
2. **Given** no device exists with the requested identifier, **When** an operator requests deletion, **Then** the system returns a clear not-found error.
3. **Given** a device has been deleted, **When** an operator attempts to retrieve or update that same identifier, **Then** the system returns a not-found error.

---

### Edge Cases

- What happens when two devices share the same name but different IP addresses? (Names are not required to be unique — IP + port combination is the uniqueness constraint.)
- How does the system handle a port number submitted as a non-numeric string?
- What happens if the password field is omitted during an update? (Omitted password on update should retain the existing credential without change.)
- What happens if an operator attempts to register a device with a loopback or reserved IP address (e.g., 127.0.0.1)?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow operators to register a new device by providing name, IP address, HTTP port number, username, and password.
- **FR-002**: System MUST enforce that the combination of IP address and HTTP port is unique across all registered devices.
- **FR-003**: System MUST validate that the IP address is a well-formed IPv4 address.
- **FR-004**: System MUST validate that the HTTP port number is an integer in the range 1–65535.
- **FR-005**: System MUST assign a unique, system-generated identifier to each registered device.
- **FR-006**: System MUST allow operators to retrieve a single device by its unique identifier.
- **FR-007**: System MUST allow operators to retrieve a complete list of all registered devices (no pagination required; maximum ~20 devices).
- **FR-008**: System MUST allow operators to update any combination of a device's fields (name, IP address, port, username, password) by its unique identifier.
- **FR-009**: System MUST allow operators to delete a device by its unique identifier.
- **FR-010**: System MUST NEVER include the device password in any response payload.
- **FR-011**: System MUST store device passwords using reversible symmetric encryption, so that the plaintext credential can be recovered for future device communication.
- **FR-012**: System MUST return clear, human-readable error messages for validation failures, conflicts, and not-found conditions.
- **FR-013**: When a password field is omitted or left blank during an update, the system MUST retain the existing password without change.
- **FR-014**: The system MUST support a persistent storage mode where device registrations survive application restarts and crashes.
- **FR-015**: The system MUST support an in-memory storage mode for use in testing environments, switchable without code changes.

### Key Entities *(include if feature involves data)*

- **Device**: Represents a single Hikvision face recognition terminal. Attributes: unique identifier, name, IP address, HTTP port number, username, password (write-only), creation timestamp, last-updated timestamp.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Operators can register, retrieve, update, and delete a device in under 3 seconds per operation under normal load.
- **SC-002**: All four CRUD operations complete successfully with valid inputs 100% of the time in a stable environment.
- **SC-003**: Invalid or incomplete input is rejected with a descriptive error message in 100% of cases — no silent failures or generic errors.
- **SC-004**: Device passwords are never exposed in any API response, verified across all endpoints.
- **SC-005**: Attempting to register two devices with the same IP address and port is blocked with a conflict message 100% of the time.

## Assumptions

- The API is single-tenant; there is no concept of organizations, teams, or per-user device ownership in this version.
- Authentication and authorization for the API itself (who can call these endpoints) is out of scope for this feature and will be addressed separately.
- Device names are not required to be unique; the IP address + port combination serves as the uniqueness constraint.
- The password field must never be returned in any response, but must be stored using reversible symmetric encryption (not a one-way hash) so the plaintext can be recovered when the system later communicates with physical devices.
- IPv6 addresses are out of scope for this version; only IPv4 is supported.
- Soft-delete (marking as inactive rather than removing) is out of scope; deletion is permanent.
- The API will be built using .NET 10 as specified by the requester.
- The API is a pure configuration registry — it stores and manages device records only. Direct communication with physical Hikvision terminals (commands, status queries, data sync) is out of scope for this version.
- Device registrations must survive application restarts and crashes (persistent storage). An in-memory storage mode must also be supported for testing environments.
- The system is designed for small deployments of up to approximately 20 devices (single site). Pagination of the device list is not required.
- The API is intended for use on isolated or internal networks; HTTPS/TLS is not required.
- No application-level logging is required; observability is handled by external tooling if needed.

## Clarifications

### Session 2026-04-05

- Q: Does this API only store device configuration records, or also communicate with physical terminals? → A: Pure registry — stores device config only; no communication with physical terminals.
- Q: Must device registrations survive application restarts? → A: Persistent storage by default (data survives restarts); in-memory mode also supported for testing environments.
- Q: Roughly how many devices will this API manage? → A: Small scale — up to ~20 devices (single site); no pagination required.
- Q: Is HTTPS/TLS required for client-to-API communication? → A: HTTP only; API is deployed on isolated/internal networks.
- Q: Is application-level logging or auditing required? → A: No logging; observability deferred to external tooling.
