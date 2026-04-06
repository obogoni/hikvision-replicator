# ADR 002 — Domain Model Validation via Result Pattern and Value Objects

## Status

Accepted — 2026-04-05

## Context

Domain objects need a reliable construction pattern that enforces invariants at the point of creation. Throwing exceptions for validation failures is noisy, makes error handling implicit, and conflates programming errors with expected domain failures. A more explicit approach — returning typed results — makes the success/failure contract visible at the call site and forces callers to handle both cases.

Additionally, complex primitive values such as IP addresses carry their own validation rules. Scattering that validation across the codebase leads to duplication and drift. Encapsulating these values as first-class types eliminates the problem.

## Decision

### 1. Domain entities — static `Create` + private constructor

Every domain class uses a **private constructor**. The only way to construct an instance is through a **static `Create` method** that validates its inputs and returns `Result<T>` from the `CSharpFunctionalExtensions` package.

- On success: `Result.Success(new T(...))`
- On failure: `Result.Failure<T>(Errors.SomeConst)` — no exceptions thrown for domain validation

### 2. Error strings co-located in a nested `Errors` class

Error messages are `public const string` fields on a **nested `public static class Errors`** defined inside the domain class. This keeps the error contract adjacent to the code that produces it and avoids a global error registry.

### 3. Complex primitives as Value Objects

Values that carry their own validation rules (IP address, port, hostname, etc.) are modeled as **Value Objects** by extending `ValueObject` from `CSharpFunctionalExtensions`. They follow the identical `Create` + `Errors` pattern. Domain entities accept and store the typed value object, not the raw primitive.

### 4. Package

`CSharpFunctionalExtensions` is added as a NuGet dependency to every project that defines domain models or value objects.

---

### Example — Value Object

```csharp
public class IpAddress : ValueObject
{
    public string Value { get; }

    private IpAddress(string value) => Value = value;

    public static Result<IpAddress> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<IpAddress>(Errors.Required);

        if (!System.Net.IPAddress.TryParse(value, out _))
            return Result.Failure<IpAddress>(Errors.InvalidFormat);

        return Result.Success(new IpAddress(value));
    }

    protected override IEnumerable<IComparable> GetEqualityComponents()
    {
        yield return Value;
    }

    public static class Errors
    {
        public const string Required = "IP address is required.";
        public const string InvalidFormat = "IP address format is invalid.";
    }
}
```

### Example — Domain Entity

```csharp
public class Device
{
    public string Name { get; }
    public IpAddress IpAddress { get; }

    private Device(string name, IpAddress ipAddress)
    {
        Name = name;
        IpAddress = ipAddress;
    }

    public static Result<Device> Create(string name, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Device>(Errors.NameRequired);

        var ipResult = IpAddress.Create(ipAddress);
        if (ipResult.IsFailure)
            return Result.Failure<Device>(ipResult.Error);

        return Result.Success(new Device(name, ipResult.Value));
    }

    public static class Errors
    {
        public const string NameRequired = "Device name is required.";
    }
}
```

## Consequences

**Positive:**
- Validation failures are explicit and typed at every call site — callers cannot ignore them.
- Error messages are versioned alongside the code that produces them; renaming a field and its errors is a single-file change.
- Value objects eliminate duplicated validation logic and make the domain model self-documenting (a property typed `IpAddress` is self-explanatory).
- No dependency on a validation framework — the pattern is pure C#.

**Negative / trade-offs:**
- EF Core cannot construct private-constructor entities directly; a separate EF projection or owned-entity mapping strategy is required.
- Callers must unwrap `Result<T>` before using the value, adding a small amount of ceremony at use sites.
