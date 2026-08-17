# Test Patterns

## Choosing the test level

Source: `.specs/STATE.md` — **AD-024** (active, 2026-08-12), which amends AD-019's
"integration is the default level" clause, and **AD-026** (active, 2026-08-17), which gives
each level its own project.

**The level is chosen by layer, not uniformly — and the project you put a test in is what
declares the level you chose.**

| Layer | Level | Project | Depth |
|---|---|---|---|
| Pure logic with no I/O — domain aggregates, value objects, `EncryptionService`, options validation | **unit** | `src/HikvisionReplicator.Tests/`, folders mirroring the Api tree (`Domain/`, `Infrastructure/`) | All branches, 1:1 with the spec's acceptance criteria, every listed edge case |
| Anything touching I/O or wiring — feature slices and their routes, repositories and specifications, startup behaviour, cross-cutting handlers | **integration** | `src/HikvisionReplicator.IntegrationTests/`, in-process through the HTTP surface against Testcontainers PostgreSQL | Every route: happy path, every listed edge case, every documented error path |
| The HTTP surface out of process | **e2e** | `src/HikvisionReplicator.E2E/` | A thin confirmation of each route — one happy path and one error path. Not a coverage layer |

Two rules keep the split honest:

- The unit project **references neither Testcontainers nor a web host**, so the pure-logic
  tests run without Docker — that is the fast feedback loop, and it is enforced by what
  the project can compile rather than by a marker a new test might forget.
- Unit tests **add depth; they never replace endpoint coverage.** Every route keeps its
  acceptance-criterion coverage at the integration layer, so a branch can never be
  unit-tested but unproven through the API.

## Test isolation in the integration project

The integration suite runs several hosts in one process, and some diagnostics are
**process-wide rather than per-host**. An OpenTelemetry `TracerProvider` installs its
listener on the global `Microsoft.AspNetCore` `ActivitySource`, so an in-memory span
exporter receives spans from *every* host alive in the process — including test classes in
a different xUnit collection running in parallel.

Assert on something that identifies your own traffic. `TracingTests` sends a `traceparent`
for a trace only it provokes and filters spans on that trace id; asserting on a
process-wide collection without such a filter passes or fails on scheduling luck.

The same caution applies to any other ambient sink — static loggers, `ActivitySource`
listeners, environment variables.

Why split at all: branch-level domain behaviour is only observable indirectly through
HTTP. The two defects the rewrite fixed in `Device` — IP normalization and the
`UpdatedAt` change-guard — are exactly that shape, and "no change means no touch" is not
cleanly assertable through a round-trip.

## Naming Tests

Source: [You are naming your tests wrong!](https://enterprisecraftsmanship.com/posts/you-naming-tests-wrong/)

### Rule

Name tests in plain English describing the **behavior**, not the implementation. Words separated by underscores. No rigid template required.

```
[Subject]_[behavior in plain English]
```

The subject can be omitted when it is obvious from the test class name.

### What NOT to do

- Do not embed HTTP verbs (`Post_`, `Get_`, `Put_`, `Delete_`)
- Do not embed status codes (`_Returns201`, `_Returns404`)
- Do not embed the method or endpoint name under test — renaming a method should never require renaming a test
- Do not use the `[MethodUnderTest]_[Scenario]_[ExpectedResult]` formula

### Examples

| Avoid | Prefer |
|---|---|
| `Post_ValidDevice_Returns201WithLocationAndBody` | `New_device_is_created_and_returned` |
| `Post_DuplicateIpAndPort_Returns409` | `Device_with_duplicate_ip_and_port_is_rejected` |
| `GetAll_NoDevices_Returns200WithEmptyArray` | `Listing_devices_with_none_registered_returns_empty` |
| `GetById_UnknownId_Returns404` | `Getting_unknown_device_returns_not_found` |
| `Delete_ThenGet_Returns404` | `Deleted_device_is_no_longer_retrievable` |
| `Post_SameExternalRef_Returns200WithUpdatedBody` | `Upserting_existing_user_updates_and_returns_them` |
| `Post_MissingExternalRef_Returns400WithFieldError` | `User_without_external_ref_is_invalid` |
| `Post_ResponseDoesNotIncludeFacePic` | `User_response_never_includes_face_picture` |

### Test class naming

Group by resource and test scope:

- `DeviceEndpointsTests` — integration tests for device HTTP endpoints
- `UserEndpointsTests` — integration tests for user HTTP endpoints

Class names carry no level suffix — the project does. `DeviceEndpointsTests` therefore
exists in both `HikvisionReplicator.IntegrationTests` and `HikvisionReplicator.E2E`, and
the assembly tells them apart.
