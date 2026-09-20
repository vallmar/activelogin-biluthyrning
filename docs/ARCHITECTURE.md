# Architecture

## Purpose

This project demonstrates a small car-rental SaaS with a clear separation between the SaaS itself and its customers.

The key idea is that **each customer can choose its persistence technology**. The SaaS owns the business API and rental rules, while a tenant-specific persistence adapter owns how that tenant's rental data is stored.

The reference implementation ships with two deliberately different base cases:

- JSON - straightforward JSON files.
- PDF - every pickup and return is persisted as a PDF document.

A customer that needs PostgreSQL, SQL Server, S3, an ERP, or another storage technology can be added as a separate IRentalStore implementation. This is the intended commercial extension point: **manual persistence integration is a service, not a reason to complicate the core application.**

## System boundary

```text
Customer A ─────HTTP─────┐
Customer B ─────HTTP─────┤
                         ▼
                    CarRental.Api
                         │
                  JWT authentication
                         │
                    tenantId
                         │
                  RentalService
                         │
                  IRentalStoreResolver
                         │
             ┌───────────┴───────────┐
             ▼                       ▼
       Customer A store        Customer B store
          PDF files              JSON files
```

Customers are external systems. Their UI and application models remain their own concerns.

## Projects

### CarRental.Domain

Contains the core business model and business rules.

- No project references.
- Must not depend on HTTP, databases, ASP.NET Core, or customer applications.
- Business rules remain testable without infrastructure.
- A rental carries its owning TenantId as part of the persisted aggregate.

### CarRental.Application

Coordinates application use cases and defines the persistence boundary.

- Depends on CarRental.Domain.
- Does not depend on ASP.NET Core or a persistence technology.
- IRentalStore is the persistence port.
- IRentalStoreResolver selects the store for the authenticated tenant.
- The tenant ID is passed explicitly into application operations.

The important abstraction is therefore not one global repository, but:

```text
tenant
  ↓
tenant-specific store
```

This makes different customers genuinely capable of using different persistence technologies.

### CarRental.Infrastructure

Contains persistence implementations and the resolver.

Current implementations:

```text
JsonRentalStore
PdfRentalStore
ConfiguredRentalStoreResolver
```

JsonRentalStore stores a rental as JSON.

PdfRentalStore stores pickup and return snapshots as separate PDF documents. The PDF document metadata contains the machine-readable rental snapshot, while the visible page contains a human-readable rental summary. This makes the PDF itself the source of truth for this deliberately unusual showcase persistence implementation.

The PDF implementation uses PDFsharp Core, which supports .NET 8 and cross-platform .NET deployments.

### CarRental.Api

The HTTP entry point and composition boundary.

- Validates JWT bearer access tokens.
- Reads tenant identity from the trusted client_id claim.
- Passes the tenant ID explicitly into RentalService.
- Does not contain persistence-specific logic.
- Configures which persistence provider each tenant receives.

### CarRental.Contracts

Contains the public HTTP request/response DTOs and public enum values.

Tenant identity is intentionally not part of these JSON DTOs; it comes from authentication.

### CarRental.Tests

Tests domain behaviour, pricing, application orchestration, API contracts, authentication, tenant isolation, JSON persistence, PDF persistence and observability.

## Customer-specific persistence

This is the important design decision.

A request does not go through one global repository. It goes through the authenticated tenant and then through the store configured for that tenant.

```text
HTTP
 ↓
authenticated tenant
 ↓
RentalService
 ↓
IRentalStoreResolver
 ↓
the store configured for that tenant
```

For example:

```json
{
  "Persistence": {
    "Tenants": {
      "tenant-a": { "Provider": "pdf" },
      "tenant-b": { "Provider": "json" }
    }
  }
}
```

Therefore tenant-a uses PdfRentalStore and tenant-b uses JsonRentalStore.

Adding a new customer-specific persistence technology becomes:

```text
1. implement IRentalStore
2. register the provider name
3. configure the tenant
4. add provider tests
```

The rental domain does not change. The API does not change. The Application service does not change.

## Why the PDF example is intentionally unusual

PDF is not a sensible primary database for a high-volume rental SaaS.

That is exactly why it is a useful reference implementation.

It proves that the application does not secretly depend on SQL, PostgreSQL, Entity Framework or JSON.

Each pickup creates:

```text
<booking-hash>.pickup.pdf
```

Each return creates:

```text
<booking-hash>.return.pdf
```

The pickup PDF is kept, so both lifecycle events remain visible as documents.

A real customer could instead provide PostgreSQLRentalStore, SqlServerRentalStore, S3RentalStore, SharePointRentalStore or ErpRentalStore without modifying the rental domain.

## Customer integration service

The intended commercial model is:

```text
Standard SaaS
    ├── API
    ├── rental rules
    ├── authentication
    └── standard persistence adapters

Customer-specific integration
    └── custom IRentalStore implementation
```

If a customer needs every rental written into an existing PostgreSQL database, build CustomerXPostgresRentalStore : IRentalStore and configure that tenant to use it.

That keeps customer-specific complexity at the edge.

## Dependency rules

Allowed production dependencies:

```text
CarRental.Application -> CarRental.Domain
CarRental.Infrastructure -> CarRental.Application
CarRental.Api -> CarRental.Application
CarRental.Api -> CarRental.Infrastructure
CarRental.Api -> CarRental.Contracts
```

Domain points to nothing. Application never points to Infrastructure. Domain never points to Application. Contracts never point to Domain/Application/Infrastructure/Api. Customers never reference SaaS projects; they use HTTP.

## Authentication and tenant isolation

The request flow is:

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
RentalService(tenantId, ...)
          │
          ▼
IRentalStoreResolver.Resolve(tenantId)
          │
          ▼
Tenant-specific persistence
```

There is deliberately no ITenantContext or tenant middleware in the Application flow.

The tenant is visible at the call site, for example service.RegisterPickupAsync(tenantId, ...). This is simpler for the current application and makes the security-critical dependency explicit.

## Persistence and tenant isolation

Each store instance is scoped to one tenant directory.

The store API therefore does not accept a tenant ID:

```csharp
store.GetAsync(bookingNumber)
```

The resolver has already selected the correct tenant-specific store.

This is an important safety property: the Application chooses the tenant store once, and the store cannot accidentally query another tenant.

## Observability

The service uses ILogger with a simple daily file provider for this showcase. Unexpected exceptions are logged at Error level. Rejected HTTP requests are logged at Warning level.

Persistence adapters should also log enough context to diagnose storage failures without logging secrets or bearer tokens.

## Future production evolution

The current adapters are deliberately simple.

For production, a database-backed store should add:

```text
database uniqueness constraint
concurrency control
transactions
durable migrations
backup
retry policy
idempotency
```

The Application and Domain layers should not need to know whether the customer uses JSON, PDF, PostgreSQL, SQL Server, S3 or an ERP.

That is the point of the persistence boundary.