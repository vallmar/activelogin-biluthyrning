# Architecture Decisions

This file records decisions that explain why the project is structured the way it is. It is intentionally short and should grow only when an important architectural choice is made.

## ADR-001: HTTP is the customer boundary

**Decision:** Customers integrate with the SaaS through HTTP. Customer projects do not reference SaaS implementation projects.

**Why:** The API is the actual product boundary. This keeps customer applications independent of internal implementation and lets their persistence and application models evolve independently.

**Consequence:** Customer examples must behave like external clients and must not take shortcuts through shared Domain/Application/Infrastructure code.

## ADR-002: Keep the core independent of infrastructure

**Decision:** Domain has no project references. Application depends on Domain. Infrastructure implements application-defined infrastructure ports.

**Why:** Business rules should not be coupled to a database or web framework, and infrastructure should be replaceable without rewriting the business model.

**Consequence:** Persistence abstractions belong at the Application boundary; implementations belong in Infrastructure.

## ADR-003: Public contracts are separate from the Domain

**Decision:** Customer-facing request/response DTOs and enum values live in `CarRental.Contracts` rather than exposing Domain objects directly.

**Why:** Internal domain evolution should not automatically change the public API.

**Consequence:** Public API changes require coordinated changes to Contracts, API serialization, tests, customer examples where needed, and `docs/CUSTOMER_API.md`.

## ADR-004: Prefer concrete designs over speculative abstractions

**Decision:** Add abstractions only when there is a concrete reason such as a real boundary, multiple implementations, or a useful testing seam.

**Why:** Unnecessary indirection makes a small system harder to understand and maintain without providing a real benefit.

**Consequence:** The architecture intentionally does not introduce CQRS, MediatR, generic repositories, event sourcing, elaborate tenant frameworks, or similar patterns without a concrete requirement.

## ADR-005: Technology baseline is .NET 8

**Decision:** The repository uses the .NET 8 SDK baseline defined in `global.json` (`8.0.400`) and targets `net8.0`.

**Why:** A fixed development baseline makes builds reproducible and prevents agents from changing framework versions as incidental cleanup.

**Consequence:** SDK/framework upgrades are deliberate changes and should include an explicit reason and relevant documentation.

## ADR-006: Observability starts simple

**Decision:** Establish a simple baseline for request/operation observation and unexpected error logging before introducing advanced telemetry.

**Why:** A service that cannot explain failures is difficult to operate and debug, even when its business logic is correct.

**Consequence:** The service uses the built-in `ILogger` abstraction. Unexpected failures are logged at Error level, rejected HTTP requests are logged at Warning level, and blocked cross-tenant access is logged as a structured Warning. The current implementation writes to a daily local file rather than introducing a third-party logging stack.

## ADR-007: Tenant identity is explicit at the application boundary

**Decision:** Tenant identity is established by a validated JWT bearer access token. The token's trusted `client_id` claim becomes the tenant ID passed explicitly into Application operations.

**Why:** Tenant identity must not be caller-controlled business data. Passing the tenant explicitly is simpler for this small application than an ambient tenant context and makes the security-critical dependency visible at the call site.

**Consequence:** `CarRental.Api` owns authentication and token-to-tenant translation. `CarRental.Application` does not know about JWTs and does not need an `ITenantContext` abstraction. Customer JSON contracts contain no tenant identifier.

## ADR-008: Showcase token issuer is intentionally local and limited

**Decision:** The API includes a small `/oauth/token` endpoint solely to make the showcase self-contained. It validates two hardcoded demo client credentials and issues short-lived signed JWTs using a development-only signing key from configuration.

**Why:** The purpose is to demonstrate realistic JWT validation and tenant propagation without introducing a full identity provider into a small reference project.

**Consequence:** This is not production authentication infrastructure. A real deployment would use an external OAuth 2.0/OIDC identity provider and the API would validate tokens issued by that provider.

## ADR-009: Tenant-specific persistence is resolved at runtime

**Decision:** The Application layer depends on `IRentalStore`, while `IRentalStoreResolver` selects a store implementation for the authenticated tenant. The reference implementation supports JSON and PDF providers.

**Why:** Different customers may have materially different persistence requirements. The SaaS should support those differences without coupling the Domain or Application service to PostgreSQL, SQL Server, JSON, PDF, or a customer's ERP.

**Consequence:** Adding a customer-specific persistence integration means implementing `IRentalStore`, registering the provider and configuring the tenant. The core rental model and API do not change. Customer applications may still own their own local persistence independently.

## ADR-010: Use a simple daily file logger for the showcase

**Decision:** For the current project, `ILogger` writes application logs to `.log/`, with one file per local calendar day.

**Why:** The project needs observable failures without adding Serilog, OpenTelemetry, Application Insights, or another external logging platform. A daily file is enough to inspect local behaviour and demonstrate that operational logging exists.

**Consequence:** The logger is intentionally small and not a production logging solution. The `.log/` directory is ignored by source control. A production deployment could replace the provider while keeping the `ILogger` abstraction unchanged.
