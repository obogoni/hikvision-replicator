# Data Model: Hikvision Device Management API

**Date**: 2026-04-05  
**Branch**: `001-hikvision-device-api`

## Entities

### Device

Represents a single Hikvision face recognition terminal registered in the system.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | int | Primary key, auto-incremented by the database | |
| `Name` | string | Required, max 100 chars | Human-readable label; not required to be unique |
| `IpAddress` | string | Required, valid IPv4 format | e.g., `192.168.1.50` |
| `HttpPort` | int | Required, 1–65535 | Default Hikvision HTTP port is 80 |
| `Username` | string | Required, max 100 chars | Credential for authenticating to the physical device |
| `EncryptedPassword` | string | Required, never returned in responses | AES-256 ciphertext of the device password; decryptable for device communication |
| `CreatedAt` | DateTime (UTC) | System-set on creation, immutable | |
| `UpdatedAt` | DateTime (UTC) | System-set on every update | |

### Uniqueness Constraint

The combination of `IpAddress` + `HttpPort` must be unique across all Device records. A device on `192.168.1.50:80` and one on `192.168.1.50:8080` are considered distinct.

### Identity Rules

- `Id` is an auto-incremented integer assigned by the database at creation time.
- Clients must not supply `Id` on create requests.
- `Id` is immutable after creation.

### Lifecycle / State Transitions

```
[Not Registered]
      │
      ▼  POST /api/devices
  [Registered] ──── PUT /api/devices/{id} ──▶ [Updated]
      │                                             │
      └──────────────────────────────────────── (same record)
      │
      ▼  DELETE /api/devices/{id}
   [Deleted]  (permanent — record removed from storage)
```

Devices have no intermediate states (e.g., no "disabled" or "pending" status in this version).

## DTOs (API Contract Shape)

### CreateDeviceRequest

```
name         string   required
ipAddress    string   required
httpPort     int      required
username     string   required
password     string   required
```

### UpdateDeviceRequest

All fields optional. Omitted fields retain existing values. A blank/empty `password` retains the existing credential.

```
name         string   optional
ipAddress    string   optional
httpPort     int      optional
username     string   optional
password     string   optional
```

### DeviceResponse

Returned by all read and write operations. `encryptedPassword` is never included.

```
id           int      
name         string   
ipAddress    string   
httpPort     int      
username     string   
createdAt    DateTime (ISO 8601 UTC)
updatedAt    DateTime (ISO 8601 UTC)
```

## Validation Rules

| Field | Rule |
|-------|------|
| `name` | Non-empty string, max 100 characters |
| `ipAddress` | Must match IPv4 pattern: four octets 0–255 (e.g., `\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}`) |
| `httpPort` | Integer in range 1–65535 |
| `username` | Non-empty string, max 100 characters |
| `password` (on create) | Non-empty string |
| `password` (on update) | If provided, non-empty; if omitted or null, retain existing |
