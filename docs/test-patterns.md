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
| Anything touching I/O or wiring — feature slices and their routes, startup behaviour, cross-cutting handlers | **integration** | `src/HikvisionReplicator.IntegrationTests/`, in-process through the HTTP surface against Testcontainers PostgreSQL | Every use case: happy path, every listed edge case, every documented error path |

Three rules keep the split honest:

- The unit project **references neither Testcontainers nor a web host**, so the pure-logic
  tests run without Docker — that is the fast feedback loop, and it is enforced by what
  the project can compile rather than by a marker a new test might forget.
- Unit tests **add depth; they never replace endpoint coverage.** Every route keeps its
  acceptance-criterion coverage at the integration layer, so a branch can never be
  unit-tested but unproven through the API.
- **Integration tests drive use cases, not dependencies** — see the next section.

## Integration tests are black box (AD-036)

**An integration test drives a use case through the HTTP surface and asserts what a caller
can observe.** It does not construct a repository, a specification, or a `DbContext` in
order to assert against it. Those are how the use case is built, not what it promises, and
a test that names them fails when the design changes rather than when the behaviour does.

Reading the database directly is **not** the same thing and is fine: `UserApiTests` exposes
`StoredUserAsync`, `StoredPictureAsync` and `CountUsersAsync` precisely because a promise
about what is *stored* cannot be proved by asking the API that stores it. Drive through
HTTP; verify wherever the truth lives.

### The one exception, and the test it must pass

A test may go below the HTTP surface **only if it can name, in a sentence, an observable
that HTTP cannot distinguish** — that is, a wrong implementation and a right one would
return byte-identical responses. Those tests live in exactly two classes, and the sentence
goes in the test's own doc comment:

- `UserPersistenceContractTests`
- `DevicePersistenceContractTests`

The four kinds that qualify today, and why HTTP is blind to each:

| Kind | Why HTTP cannot see it |
|---|---|
| **What a read touches** | A response that omits the face bytes looks identical whether or not they were loaded. Only the emitted SQL discriminates — and these assertions are the only thing enforcing A-1, on the latency path AD-014 makes primary. |
| **The shape of the two unique indexes** | Their asymmetry is deliberate and easy to misread. Swapping the filters leaves most round-trips green; `pg_indexes` does not lie. |
| **Which failures are *not* translated** | AD-022 turns two named index violations into conflicts and everything else must stay an exception. Provoking a foreign constraint violation or a vanished row needs the database, not a request. |
| **Cancellation, and the index→message mapping** | A pre-cancelled token proves the abort deterministically. The mapping is reachable through HTTP *only* when a racer slips past the service pre-check — which is scheduling, not evidence (AD-026). |

That last row is the cautionary one. The mapping **is** asserted by the race tests in
`UserRegistrationTests`, and they do catch a swapped mapping — but swapping it was observed
failing two of them on one run and one on the next. **A guard that depends on thread
scheduling is not a guard.** When a use-case test can only reach something by racing, prove
it deterministically as well.

If a test you are about to add cannot state its blind-spot sentence, it belongs in a
use-case class instead.

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

## There is no end-to-end level

The `HikvisionReplicator.E2E` project was removed (AD-035). Every one of its 17 tests asserted
something an integration test already asserted, and it ran in neither gate. **Do not add tests at
a third level, and do not reinstate the project, until the unit and integration conventions above
are settled and there is a deployment for a suite to smoke.**

The gap a real end-to-end suite would close is narrow and worth naming, because it is *not* route
behaviour: `WebApplicationFactory` injects configuration rather than reading it, runs as
environment `Test`, and serves requests through an in-memory `TestServer` with no socket. What
lives outside that boundary is a shipped process finding its own config and its own key. When
that becomes worth testing, it is a **deployment smoke test** — not a second copy of the route
assertions.

The one thing `TestServer` genuinely cannot answer is already covered in-process:
`KestrelWebApplicationFactory` puts the application on a real socket for the request-size limit,
and its class comment explains why nothing else should join it there.

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

Name a class for the **use case or situation** it covers, not for the component it exercises:

- `UserRegistrationTests`, `UserAmendmentTests`, `UserResurrectionTests` — the three
  situations the one `PUT /api/users/{externalRef}` upsert route serves
- `UserRemovalTests`, `UserLookupTests`, `UserCatalogueTests` — one per remaining route
- `DeviceEndpointsTests` — the five device routes
- `UserPersistenceContractTests`, `DevicePersistenceContractTests` — the below-HTTP
  exception above, and the only classes named after a mechanism

Class names carry no level suffix — the project does.
