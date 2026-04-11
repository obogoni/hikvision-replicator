# ADR 002: Implementation of the Result Pattern using OneOf and Domain Error Hierarchy

## Status
Accepted

## Context
In our .NET Core application, we need a consistent way to communicate the outcome of business operations from the **Domain/Application Layer** to the **Presentation Layer (Web API)**. 

We aim to satisfy three main requirements:
1. **Separation of Concerns:** The Domain Layer must not have knowledge of HTTP semantics (e.g., `IResult`, `HttpStatusCode`).
2. **Type Safety:** The compiler should ideally help ensure that all possible success and failure outcomes are handled.
3. **Signature Clarity:** Method signatures should remain readable and maintainable, even as the number of potential error conditions grows.

## Decision
We will implement the **Result Pattern** using the `OneOf` library combined with a **Sealed Domain Error Hierarchy**.

### Key Implementation Details:
* **The Return Type:** Use cases will return a `OneOf<TValue, DomainError>`. This limits the signature to two generic arguments regardless of the number of specific errors.
* **The Error Hierarchy:** Errors will be defined as a hierarchy of C# `records` (e.g., `public abstract record DomainError; public record UserNotFound(Guid Id) : DomainError;`).
* **The Mapping Layer:** We will use an extension method in the API layer (`ToMinimalApiResult`) containing a `switch expression` to translate `DomainError` subtypes into specific ASP.NET Core `IResult` responses.

## Consequences

### Positive (Pros)
* **Purity:** The core logic remains entirely decoupled from the web framework.
* **Predictability:** No more "hidden" exceptions for expected business failures. The method signature explicitly states that it might return an error.
* **Developer Experience:** Using `OneOf.Match()` or `Switch()` ensures that developers account for both the success and error paths.
* **Maintainability:** By grouping errors under a `DomainError` base type, we avoid "generic soup" (e.g., `OneOf<T, E1, E2, E3, E4...>`).

### Negative (Cons)
* **External Dependency:** The Core layer now has a dependency on the `OneOf` NuGet package.
* **Learning Curve:** New team members must learn the `Match`/`Switch` syntax provided by the library.
* **Boilerplate:** Requires creating small record classes for each unique domain error scenario.

## Alternatives Considered
* **Standard `Result<T>` Object:** Dismissed because it lacks compile-time enforcement to handle specific error codes, often resulting in "string-ly typed" error checking.
* **Exceptions for Flow Control:** Dismissed as an anti-pattern that hides business logic and incurs a performance penalty.
* **Raw `OneOf<T, Error1, Error2...>`:** Dismissed because method signatures became too large and difficult to read as the application grew.

---

**Date:** 2026-04-10  
**Author:** Gemini AI & Development Team  
**Tags:** #architecture #dotnet #result-pattern #clean-architecture #oneof