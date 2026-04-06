# Quickstart: Hikvision Device Management API

**Branch**: `001-hikvision-device-api`

## Prerequisites

- .NET 10 SDK
- Any HTTP client (curl, Postman, or browser DevTools)

## Running the API

```bash
# From the repository root

# Restore dependencies
dotnet restore

# Apply database migrations (first run only)
dotnet ef database update --project src/HikvisionReplicator.Data --startup-project src/HikvisionReplicator.Api

# Run the API
dotnet run --project src/HikvisionReplicator.Api
```

The API starts on `http://localhost:5000` by default (configurable in `appsettings.json`).

## Configuration

`appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=devices.db"
  }
}
```

To run with an in-memory database (no file persistence — useful for local testing):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=:memory:"
  }
}
```

## Example Requests

### Register a device
```bash
curl -X POST http://localhost:5000/api/devices \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Entrance Terminal",
    "ipAddress": "192.168.1.50",
    "httpPort": 80,
    "username": "admin",
    "password": "secret123"
  }'
```

### List all devices
```bash
curl http://localhost:5000/api/devices
```

### Get a device by ID
```bash
curl http://localhost:5000/api/devices/1
```

### Update a device
```bash
curl -X PUT http://localhost:5000/api/devices/1 \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Main Entrance",
    "httpPort": 8080
  }'
```

### Delete a device
```bash
curl -X DELETE http://localhost:5000/api/devices/1
```

## Running Tests

```bash
dotnet test src/HikvisionReplicator.Tests
```

Tests use SQLite in-memory mode automatically — no database file is created.
