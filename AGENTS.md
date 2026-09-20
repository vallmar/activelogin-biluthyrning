# AGENTS.md

## Purpose

This repository is a reference car-rental SaaS project. Agents must preserve the architecture, public API contract, and customer boundary while making useful, small changes.

This file is the repository constitution. More detailed documentation lives in `docs/` and executable rules belong in tests.

## 1. Public HTTP/JSON contract is sacred

The public contract is the JSON and HTTP API exposed to customers.

- Never silently change the customer-facing contract.
- A contract change includes request/response fields, property names, types, enum values, serialization shape, HTTP status codes, or error response format.
- If the JSON sent to or received from a customer changes, update `docs/CUSTOMER_API.md` in the same change.
- Add or update API integration/contract tests for the change.
- Customer examples must continue to work, or be updated as part of the same change.
- Do not expose Domain objects directly as the public API contract.
- Treat `CarRental.Contracts` as public API DTOs, not as a place to leak internal implementation details.

Before changing the API, check:

`CarRental.Contracts` → API serialization/deserialization → API integration tests → customer examples → `docs/CUSTOMER_API.md`

## 2. Dependency direction is absolute

The intended dependency direction is:

```text
Customer ──HTTP──> CarRental.Api
                       │
                       ├──> CarRental.Application ──> CarRental.Domain
                       │
                       ├──> CarRental.Infrastructure
                       │
                       └──> CarRental.Contracts
```

Rules:

- `CarRental.Domain` has no project references.
- `CarRental.Application` may depend on `CarRental.Domain`.
- `CarRental.Infrastructure` may depend on `CarRental.Application` and implement its persistence/infrastructure ports.
- `CarRental.Api` may depend on `CarRental.Application`, `CarRental.Infrastructure`, and `CarRental.Contracts`.
- Do not introduce dependencies in the opposite direction.
- Tests may reference the projects they need to test, but production projects must not depend on tests.

## 3. Customers are external systems

Everything under `customers/` represents an independent customer application.

- Customers communicate with the SaaS through HTTP only.
- Customer projects must never reference `CarRental.Domain`, `CarRental.Application`, `CarRental.Infrastructure`, or `CarRental.Api` as project/code dependencies.
- Do not share internal domain classes with customers.
- Treat each customer project as if it lived in a completely separate repository maintained by another company.
- Customer applications may own their own local persistence and models. The SaaS may also provide a managed, tenant-specific persistence adapter when the customer wants the SaaS to persist rental state on its behalf.
- Managed customer-specific persistence belongs behind IRentalStore; it must not leak into the Domain or API.

## 4. Abstractions must be earned

Do not add interfaces, generic repositories, factories, handlers, wrappers, or other abstractions just because they are common patterns.

Introduce an abstraction when there is a concrete reason such as:

- a real architectural boundary,
- multiple implementations,
- a testing seam,
- external infrastructure that should be isolated, or
- a requirement that genuinely benefits from substitutability.

Every non-trivial abstraction should have a reason that can be explained in one sentence.

Prefer the simplest design that preserves the intended boundaries.

## 5. Do not invent business behaviour silently

When requirements are unclear:

1. Check existing code and tests.
2. Check `docs/ASSUMPTIONS.md`.
3. Check `docs/CUSTOMER_API.md` for customer-visible behaviour.
4. Check `docs/DECISIONS.md` for architectural decisions.
5. If the uncertainty materially affects behaviour or architecture, stop and explain the ambiguity rather than silently inventing an important rule.

When a reasonable small assumption is necessary, document it when it becomes part of observable behaviour.

## 6. Make the smallest coherent change

- Solve the requested problem without unrelated refactoring.
- Do not rewrite working code merely to make it look more modern.
- Preserve existing behaviour unless the task explicitly changes it.
- Avoid introducing new frameworks or architectural patterns without a concrete reason.
- Keep changes easy to review and easy to explain.

## 7. Tests are part of the architecture

Tests should protect:

- domain business rules,
- application use cases,
- public HTTP/JSON behaviour,
- important error behaviour, and
- architectural dependency rules.

When changing behaviour, update the relevant tests in the same change.

When adding an architectural rule that can be checked automatically, prefer an executable architecture test over relying only on this document.

## 8. Documentation and code must agree

There are three important sources of truth:

- **Business truth:** domain behaviour, tests, and `docs/ASSUMPTIONS.md`.
- **API truth:** `CarRental.Contracts`, API behaviour/tests, and `docs/CUSTOMER_API.md`.
- **Architecture truth:** project references, architecture tests, `docs/ARCHITECTURE.md`, and `docs/DECISIONS.md`.

If these disagree, do not silently choose one. Identify the contradiction and resolve it deliberately.

## 9. Architectural deviations require an explicit reason

Do not bypass an architectural rule because it is inconvenient.

If a change genuinely requires breaking an established boundary, explain:

- which rule is being changed,
- why the existing architecture is insufficient,
- what alternative was considered, and
- what documentation and tests will be updated.

Do not hide an architectural deviation inside an otherwise unrelated feature.

## 10. Avoid cargo-cult architecture

Do not introduce CQRS, MediatR, event sourcing, generic repositories, distributed messaging, elaborate tenant frameworks, mapping frameworks, validation frameworks, distributed caches, microservices, or similar infrastructure without a concrete requirement that justifies the complexity.

The goal is a clear, maintainable architecture that solves the actual problem, not a collection of fashionable patterns.

## 11. Keep customer-facing errors intentional

Customer-facing errors are part of the API contract.

- Do not expose stack traces, internal exception details, connection strings, file paths, or other implementation details.
- Preserve documented HTTP status and error-response behaviour.
- If error shape or semantics change, update API documentation and contract tests.

## 12. Observability is a product concern

The system must not be a black box when something goes wrong.

- Unexpected application errors should be logged with enough context to diagnose the failure.
- Logs must not contain secrets or unnecessary sensitive/customer data.
- Logging should distinguish expected business/API failures from unexpected application failures.
- Do not swallow exceptions silently.
- When adding important operations or infrastructure, consider what an operator would need to understand what happened.
- Observability changes should include tests where practical, especially around error handling and API behaviour.

The first agent-driven observability task should establish a simple, sensible baseline for error logging and request/operation observation before adding more elaborate telemetry.

## 13. Technology baseline

Use the repository's existing technology choices unless the task explicitly requires changing them.

- .NET SDK: `8.0.400`, controlled by `global.json`.
- Target framework: `.NET 8` (`net8.0`).
- Do not upgrade the SDK/framework or add a major framework dependency as incidental cleanup.
- Before changing the technology baseline, explain the reason and update the relevant documentation.

## 14. Tenant isolation is mandatory

The project uses validated JWT bearer tokens to establish tenant context.

- Customer requests authenticate with `Authorization: Bearer <access_token>`.
- The showcase `/oauth/token` endpoint issues short-lived demo JWTs for two configured showcase clients; this is a local demonstration of token issuance, not a production identity provider.
- The API validates token signature, issuer, audience, and lifetime before trusting tenant identity.
- Tenant identity is derived from the validated `client_id` claim and passed explicitly into Application operations.
- Tenant identity must not be supplied as a customer-controlled JSON business field.
- Application operations must receive tenant identity from the authenticated API boundary, and persistence must be resolved globally.
- A booking number is globally unique across the API, regardless of tenant.
- If a requested booking is unavailable to the current tenant, the operation must remain non-disclosing to the caller (`404 Not Found`).
- Tests must cover authentication failure, cross-tenant isolation, and global booking-number uniqueness.

## 15. Definition of done for agent changes

Before considering a change complete:

1. Build the affected projects.
2. Run the relevant tests; preferably run the full test suite for architectural or API changes.
3. Check that public API behaviour has not changed unintentionally.
4. Check that dependency direction remains intact.
5. Check that customer projects remain HTTP-only integrations.
6. Update documentation when behaviour, contracts, assumptions, or architecture changed.
7. Keep the diff focused and explain any non-obvious design decision.
