# API Contract: Devices

**Date**: 2026-04-05  
**Branch**: `001-hikvision-device-api`  
**Base path**: `/api/devices`  
**Protocol**: HTTP (internal network only)  
**Format**: JSON request and response bodies

---

## Endpoints

### POST /api/devices — Register a Device

Register a new Hikvision face recognition terminal.

**Request body**:
```json
{
  "name": "Entrance Terminal",
  "ipAddress": "192.168.1.50",
  "httpPort": 80,
  "username": "admin",
  "password": "secret123"
}
```

**Responses**:

| Status | Condition | Body |
|--------|-----------|------|
| `201 Created` | Device registered successfully | `DeviceResponse` (see schema below) + `Location` header |
| `400 Bad Request` | Validation failure (missing/invalid field) | `ErrorResponse` with field-level details |
| `409 Conflict` | IP address + port already registered | `ErrorResponse` |

---

### GET /api/devices — List All Devices

Retrieve all registered devices. Returns an empty array if none exist.

**Request body**: none

**Responses**:

| Status | Condition | Body |
|--------|-----------|------|
| `200 OK` | Always (including empty inventory) | Array of `DeviceResponse` |

---

### GET /api/devices/{id} — Get a Device

Retrieve a single device by its unique identifier.

**Path parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `id` | int | Unique device identifier |

**Responses**:

| Status | Condition | Body |
|--------|-----------|------|
| `200 OK` | Device found | `DeviceResponse` |
| `404 Not Found` | No device with given `id` | `ErrorResponse` |

---

### PUT /api/devices/{id} — Update a Device

Update one or more fields of an existing device. All fields are optional; omitted fields retain their current values. Omitting or sending an empty `password` retains the existing credential.

**Path parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `id` | int | Unique device identifier |

**Request body** (all fields optional):
```json
{
  "name": "Main Entrance",
  "ipAddress": "192.168.1.51",
  "httpPort": 8080,
  "username": "operator",
  "password": "newpassword"
}
```

**Responses**:

| Status | Condition | Body |
|--------|-----------|------|
| `200 OK` | Device updated successfully | Updated `DeviceResponse` |
| `400 Bad Request` | Validation failure on provided fields | `ErrorResponse` with field-level details |
| `404 Not Found` | No device with given `id` | `ErrorResponse` |
| `409 Conflict` | Updated IP + port conflicts with another device | `ErrorResponse` |

---

### DELETE /api/devices/{id} — Delete a Device

Permanently remove a device from the registry.

**Path parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `id` | int | Unique device identifier |

**Responses**:

| Status | Condition | Body |
|--------|-----------|------|
| `204 No Content` | Device deleted successfully | empty |
| `404 Not Found` | No device with given `id` | `ErrorResponse` |

---

## Schemas

### DeviceResponse

```json
{
  "id": 1,
  "name": "Entrance Terminal",
  "ipAddress": "192.168.1.50",
  "httpPort": 80,
  "username": "admin",
  "createdAt": "2026-04-05T10:00:00Z",
  "updatedAt": "2026-04-05T10:00:00Z"
}
```

> `password` / `encryptedPassword` are **never** included in any response.

### ErrorResponse

```json
{
  "type": "validation_error",
  "message": "One or more fields are invalid.",
  "errors": {
    "ipAddress": ["Must be a valid IPv4 address."],
    "httpPort": ["Must be between 1 and 65535."]
  }
}
```

For non-validation errors (404, 409), `errors` may be omitted:

```json
{
  "type": "not_found",
  "message": "Device with id '42' was not found."
}
```

---

## Notes

- All timestamps are returned in ISO 8601 UTC format.
- `id` values are auto-incremented integers assigned by the database.
- The `Location` header on `201 Created` points to `/api/devices/{id}`.
- IPv6 addresses are not accepted; only IPv4 is supported in this version.
