# Test Patterns

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

E2E test classes follow the same convention under `HikvisionReplicator.E2ETests`.
