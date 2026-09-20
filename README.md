# Car Rental SaaS Reference Architecture

This repository is a reference implementation of a small car-rental SaaS.

## Customer documentation

**Customers only need to read one document:**

[Customer Integration Guide](docs/CUSTOMER_GUIDE.md)

It contains the complete onboarding path:

- authentication;
- all API endpoints and request/response contracts;
- all documented error codes;
- PDF persistence;
- Azure SQL Database setup;
- the Azure SQL schema script;
- connection-string handover;
- manual persistence integrations;
- security expectations;
- links to the automated test suites;
- production considerations.

## Persistence model

Each tenant resolves to its own persistence adapter:

```text
HTTP API
   ↓
JWT authentication
   ↓
tenant identity
   ↓
RentalService
   ↓
IRentalStoreResolver
   ↓
IRentalStore
   ↓
customer-specific persistence technology
```

The two customer-facing reference choices are:

1. **PDF** — we persist pickup and return documents.
2. **Azure SQL / SQL Server** — the customer owns the relational database and provides a secure connection string; we provide/configure the persistence adapter.

We also offer manual integration for other persistence solutions such as PostgreSQL, Oracle, ERP systems, S3/Azure Blob, REST services, or customer-specific databases.

## Developer/reference documentation

The customer guide is the canonical onboarding document. The following documents are supporting engineering references:

- `docs/ARCHITECTURE.md` — internal architecture.
- `docs/DECISIONS.md` — architecture decisions.
- `docs/CUSTOMER_API.md` — detailed API reference retained for developers.
- `docs/ERROR_CODES.md` — detailed error-code reference retained for developers.
- `docs/azure-sql-schema.sql` — customer Azure SQL schema script.

## Main projects

- `src/CarRental.Domain` — rental aggregate, state rules, categories and pricing.
- `src/CarRental.Application` — use cases, pricing and persistence ports.
- `src/CarRental.Infrastructure` — persistence adapters and tenant-specific resolver.
- `src/CarRental.Api` — HTTP, authentication and composition boundary.
- `src/CarRental.Contracts` — public HTTP/JSON contracts.
- `tests/CarRental.Tests` — automated verification.

## Tests

Run the complete suite with:

```bash
dotnet test
```

Customers can inspect the tests directly from the [Customer Integration Guide](docs/CUSTOMER_GUIDE.md).

## Design principle

Keep the business core stable and put customer-specific persistence complexity at the edge:

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

That allows different customers to use different persistence technologies without changing the public API or business rules.
