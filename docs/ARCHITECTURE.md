# Architecture

## Purpose

This project demonstrates a small car-rental SaaS with a clear separation between the SaaS itself and its customers.

The architecture is intentionally simple. It should be possible to explain the important boundaries without relying on a large number of frameworks or patterns.

## System boundary

The HTTP API is the product boundary.

```text
Customer A ─────HTTP─────┐
Customer B ─────HTTP─────┤
                         ▼
                    CarRental.Api
                         │
                  ┌──────┴──────┐
                  ▼             ▼
             Application     Contracts
                  │
                  ▼
                Domain
                  ▲
                  │
           Infrastructure
```

Customers are external systems. Their storage, UI, and application models are their own concerns.

## Projects

### `CarRental.Domain`

Contains the core business model and business rules.

- No project references.
- Must not depend on HTTP, databases, ASP.NET Core, or customer applications.
- Business rules should remain testable without infrastructure.
- A rental carries its owning `TenantId` so tenant ownership is part of the persisted aggregate, not merely an HTTP concern.

### `CarRental.Application`

Coordinates application use cases and defines ports where the application needs external capabilities.

- Depends on `CarRental.Domain`.
- Does not depend on ASP.NET Core or a specific persistence technology.
- `IRentalRepository` is an application persistence boundary.
- `ITenantContext` is the application boundary for tenant identity established by the authentication/transport layer.
- Rental lookups are always scoped through the current tenant context.
- A narrowly scoped ownership lookup exists for auditing a failed cross-tenant access attempt.

### `CarRental.Infrastructure`

Contains implementations of infrastructure concerns such as persistence.

- Depends on `CarRental.Application`.
- Implements application-defined ports.
- Infrastructure choices must not leak into the Domain.
- The in-memory repository keys rentals by `(TenantId, BookingNumber)` to demonstrate tenant isolation at the persistence boundary.

### `CarRental.Api`

The HTTP entry point and composition boundary for the SaaS.

- Depends on `CarRental.Application`, `CarRental.Infrastructure`, and `CarRental.Contracts`.
- Validates JWT bearer access tokens.
- Derives tenant identity from the trusted `client_id` claim and populates `ITenantContext`.
- Translates HTTP requests into application operations.
- Translates application/domain results into customer-facing HTTP responses.
- Must not expose internal domain objects as the public API contract.

The repository contains a deliberately small `/oauth/token` showcase endpoint so the project can demonstrate the complete token flow without an external identity provider. It is explicitly development/demo infrastructure, not production authentication.

### `CarRental.Contracts`

Contains the public HTTP API request/response DTOs and public enum values.

This project represents a customer-visible contract. Changes require corresponding API documentation and contract/integration tests.

Contracts are deliberately separate from the Domain so internal domain changes do not automatically become API changes.

Tenant identity is intentionally not part of these JSON DTOs; it is authentication context.

### `CarRental.Tests`

Tests the system, including domain behaviour, application behaviour, API contracts, authentication, security isolation, and observability.

Architecture rules that can be checked automatically should be enforced here rather than only described in prose.

## Customer applications

Customer projects live under `customers/` and are deliberately not part of the SaaS solution.

They must:

- communicate with the SaaS through HTTP,
- obtain an access token and send it as request authentication,
- own their own persistence and customer-specific models,
- map their own models to/from the public HTTP contract, and
- remain independent of the SaaS implementation.

A customer must never add a project reference to `CarRental.Domain`, `CarRental.Application`, `CarRental.Infrastructure`, or `CarRental.Api`.

The intended mental model is that each customer could be moved into a completely separate repository without changing this architecture.

## Dependency rules

The allowed production dependencies are:

```text
CarRental.Application  -> CarRental.Domain
CarRental.Infrastructure -> CarRental.Application
CarRental.Api -> CarRental.Application
CarRental.Api -> CarRental.Infrastructure
CarRental.Api -> CarRental.Contracts
```

No other production project references should be added without an explicit architectural decision.

In particular:

- Domain points to nothing.
- Application never points to Infrastructure.
- Domain never points to Application.
- Contracts never point to Domain/Application/Infrastructure/Api.
- Customers never reference SaaS projects; they use HTTP.

## Abstraction strategy

The project uses abstractions at real boundaries rather than everywhere.

An abstraction is justified when there is a concrete architectural boundary, multiple implementations, a testing seam, or another requirement that makes substitutability valuable.

This is why a repository port belongs in Application while its implementation belongs in Infrastructure, and why tenant identity has an `ITenantContext` boundary between HTTP authentication context and application use cases.

The architecture should not grow generic repositories, mediator layers, factories, handlers, mapping frameworks, or other indirection without a concrete reason.

## API boundary

The public API has three separate concerns:

1. HTTP transport, authentication and serialization in `CarRental.Api`.
2. Public DTOs/enums in `CarRental.Contracts`.
3. Customer-facing API documentation in `docs/CUSTOMER_API.md`.

Tenant identity is a transport/authentication concern: it is carried by a validated bearer token but is not included in the JSON business payload.

These must remain aligned. A public JSON or HTTP change is not complete until the contract, implementation, tests, and documentation agree.

## Authentication and tenant isolation

The intended request flow is:

```text
Authorization: Bearer <JWT>
          │
          ▼
ASP.NET JWT bearer validation
          │
          ▼
ClaimsPrincipal.client_id
          │
          ▼
TenantContextMiddleware
          │
          ▼
ITenantContext
          │
          ▼
RentalService
          │
          ▼
IRentalRepository(tenantId, bookingNumber)
          │
          ▼
Tenant-scoped persistence
```

The API validates the token's signature, issuer, audience and lifetime before trusting the `client_id` claim. The application does not know that JWT exists; it only knows the tenant context.

Tenant isolation is implemented at the persistence boundary. The same booking number may exist independently for multiple tenants. A tenant-scoped lookup therefore cannot accidentally return another tenant's rental.

If the requested booking is missing for the current tenant, the application performs a narrow ownership check. If another tenant owns the booking, it logs a security-relevant warning and still returns `404 Not Found` to the caller. This avoids disclosing the other tenant's data while giving operators evidence of a possible cross-tenant access attempt.

## Observability

Observability is part of the operational architecture, not an afterthought.

The baseline answers:

- What request/operation was being handled?
- Did it succeed or fail?
- Was the failure an expected business/API failure or an unexpected application failure?
- If unexpected, is there enough context in the logs to diagnose it?
- Did an authenticated tenant attempt to access data owned by another tenant?

The service uses the built-in `ILogger` abstraction with a deliberately simple file provider for the current project. Application logs are written under `.log/` in the API content root, with one file per local calendar day named `car-rental-yyyy-MM-dd.log`.

Unexpected exceptions are logged at Error level with the exception. Rejected HTTP requests (`400`-`499`) are logged at Warning level with status, tenant (or anonymous), method and path. Blocked cross-tenant access is logged at Warning level with tenant and booking context. Secrets and bearer tokens are never logged.

The `.log/` directory is local runtime state and is excluded from source control. More advanced telemetry should be introduced only when there is a concrete operational need.
