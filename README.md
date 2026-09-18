# Car Rental SaaS Reference Architecture

This repository is a reference implementation of a small car-rental SaaS.

The core product owns:
- the HTTP API;
- authentication and tenant identity;
- rental business rules;
- pricing;
- public API contracts.

**Each customer can choose how its rental data is persisted.**

The reference implementation includes two deliberately different persistence providers:
- JSON files.
- PDF documents.

A real customer can receive a custom persistence adapter for PostgreSQL, SQL Server, an ERP, S3, or another system. That customer-specific implementation is kept outside the core business logic.

## Architecture

```text
 Customer A ──HTTP──┐
                    │
 Customer B ──HTTP──┤
                    ▼
               Rental API
                    │
              JWT authentication
                    │
                tenantId
                    │
             RentalService
                    │
          IRentalStoreResolver
             ┌──────┴──────┐
             ▼             ▼
       Customer A      Customer B
       PDF store       JSON store
```

The important boundary is the HTTP API. Customer applications are consumers of the product; they do not reference the domain assembly.

The important persistence boundary is the tenant-specific store:

```text
tenant
  ↓
IRentalStoreResolver
  ↓
IRentalStore
  ↓
customer-specific technology
```

## Persistence providers

The showcase configuration is:

```json
{
  "Persistence": {
    "RootDirectory": "data",
    "Tenants": {
      "tenant-a": { "Provider": "pdf" },
      "tenant-b": { "Provider": "json" }
    }
  }
}
```

### JSON

JsonRentalStore stores the complete rental snapshot as JSON.

### PDF

PdfRentalStore stores each lifecycle event as a PDF:

```text
<booking-hash>.pickup.pdf
<booking-hash>.return.pdf
```

The pickup PDF is retained when the rental is returned, so both events are represented as documents.

The PDF also contains a machine-readable snapshot in its document metadata, allowing the PDF itself to act as the source of truth for this intentionally unusual persistence example.

## Adding a customer-specific persistence technology

This is intentionally simple.

Implement IRentalStore, then:

1. Add the implementation under CarRental.Infrastructure or in a customer-specific integration project.
2. Register a provider name.
3. Configure the customer/tenant to use that provider.
4. Add persistence-specific tests.

For example:

```text
CustomerXPostgresRentalStore : IRentalStore
CustomerYSqlServerRentalStore : IRentalStore
CustomerZErpRentalStore : IRentalStore
```

The Domain does not change.
The API does not change.
The RentalService does not change.

This is the intended extension point for selling manual customer integration work.

## Main projects

- src/CarRental.Domain - rental aggregate, state rules, categories and pricing data.
- src/CarRental.Application - use cases, price calculation and persistence port.
- src/CarRental.Infrastructure - JSON/PDF persistence adapters and tenant-specific store resolver.
- src/CarRental.Api - HTTP, JWT authentication and composition boundary.
- src/CarRental.Contracts - public HTTP/JSON contracts.
- tests/CarRental.Tests - domain, application, API, security, persistence and observability tests.

## Customer examples

The customer examples are intentionally separate applications. In a real SaaS deployment they could live in completely separate repositories.

- customers/CustomerA.JsonConsole - console frontend + local JSON persistence.
- customers/CustomerB.Postgres - console/API client + PostgreSQL persistence.

They communicate with the SaaS API via HTTP and remain independent of the SaaS implementation.

## Authentication and multi-tenancy

Customer API calls use JWT bearer authentication:

```http
Authorization: Bearer <access_token>
```

The API validates the JWT and derives the tenant from its trusted client_id claim.

Tenant identity is passed explicitly to RentalService, which resolves the configured persistence provider for that tenant.

The same booking number can therefore exist independently for different customers.

The /oauth/token endpoint is deliberately a tiny development/demo identity provider, not production authentication.

## Tests

The test suite covers:
- rental invariants;
- pricing;
- application orchestration;
- API contracts;
- authentication;
- tenant isolation;
- JSON persistence;
- PDF persistence;
- logging and unexpected errors.

Run:

```bash
dotnet test
dotnet run --project src/CarRental.Api
```

## Design principle

Keep the business core boring.

Customer-specific complexity belongs at the edge:

```text
API
 ↓
RentalService
 ↓
Rental Domain
 ↓
IRentalStore
 ↓
customer technology
```

That is what lets the same SaaS support customers with completely different persistence requirements without turning the core into a framework.