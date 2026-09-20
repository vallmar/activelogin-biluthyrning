# Customer Integration Guide

This is the **single guide customers need to read** to integrate with the Car Rental API.

It covers authentication, API contracts, errors, persistence choices, Azure SQL setup, testing, security, and what we can do as a manual integration service.

## 1. What you receive

The product exposes a small HTTP/JSON API for vehicle pickup and return.

Your application does **not** need to use our internal .NET code. You integrate over HTTP using the contracts in this guide.

You choose how rental data is persisted:

| Option | What it means | Customer setup |
|---|---|---|
| **PDF** | We persist pickup/return documents as PDFs on the storage configured for your deployment. | No database required. You provide/agree the storage location and retention requirements. |
| **Azure SQL / SQL Server** | Rental data is persisted in a relational database owned by you. | You create the Azure SQL database using the script below and provide us with a secure connection string. |
| **Manual integration** | We build an adapter for another persistence technology. | We agree the target technology, schema/API and credentials, then implement and test the adapter. |

The persistence technology is an implementation detail behind the API. Your HTTP contract does not change when the persistence technology changes.

## 2. Quick start

1. Choose **PDF** or **Azure SQL**.
2. If using Azure SQL, create the database and schema using section 7.
3. Give us your database connection string through the agreed secure secret-sharing method — **never commit it to GitHub or send it in source code**.
4. We configure your tenant to use the selected persistence adapter.
5. Obtain an access token using your production authentication mechanism.
6. Call the pickup endpoint.
7. Call the return endpoint.
8. Run the test suite or review the linked tests in section 10.

For the repository showcase, `/oauth/token` is a deliberately small demo token issuer. It is **not** the production authentication solution.

---

# 3. Authentication

## Production

Customer requests use OAuth 2.0/OIDC-style bearer tokens.

Send the access token on every protected API call:

```http
Authorization: Bearer <access_token>
```

The API validates the token signature, issuer, audience and lifetime. The authenticated client identity determines the tenant. Customers must not put a tenant ID into the rental JSON to select another tenant.

In a production deployment we configure the API against the agreed identity provider. We can help with that integration as part of the implementation.

## Repository demo

The repository contains:

```http
POST /oauth/token
Content-Type: application/json
```

Request:

```json
{
  "clientId": "tenant-a",
  "clientSecret": "secret-a"
}
```

Response:

```json
{
  "accessToken": "<signed-jwt>",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

The demo has two example tenants:

| Client ID | Demo secret |
|---|---|
| `tenant-a` | `secret-a` |
| `tenant-b` | `secret-b` |

These credentials are for the repository example only.

---

# 4. API contract

Base URL is deployment-specific.

## POST /oauth/token

Demo-only authentication endpoint.

### Request

```json
{
  "clientId": "tenant-a",
  "clientSecret": "secret-a"
}
```

### Success

**200 OK**

```json
{
  "accessToken": "<signed-jwt>",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

### Errors

- `401 AUTH_INVALID_CREDENTIALS` — credentials were rejected.
- `400 AUTH_INVALID_INPUT` — required authentication input was missing/invalid.

## POST /api/rentals/pickup

Registers a vehicle pickup.

### Headers

```http
Authorization: Bearer <access_token>
Content-Type: application/json
```

### Request

```json
{
  "bookingNumber": "TEST-001",
  "registrationNumber": "ABC123",
  "customerIdentifier": "customer-a",
  "category": "SmallCar",
  "pickupTime": "2026-09-15T10:00:00Z",
  "pickupOdometer": 10000
}
```

### Fields

| Field | Type | Required | Rule |
|---|---|---:|---|
| `bookingNumber` | string | Yes | Unique within the authenticated tenant |
| `registrationNumber` | string | Yes | Vehicle registration |
| `customerIdentifier` | string | Yes | Your customer identifier |
| `category` | string | Yes | `SmallCar`, `Combi`, or `Truck` |
| `pickupTime` | ISO-8601 timestamp | Yes | Pickup timestamp |
| `pickupOdometer` | integer | Yes | Must be >= 0 |

### Success

**201 Created**

```json
{
  "bookingNumber": "TEST-001",
  "registrationNumber": "ABC123",
  "customerIdentifier": "customer-a",
  "category": "SmallCar",
  "pickupTime": "2026-09-15T10:00:00Z",
  "pickupOdometer": 10000,
  "isReturned": false
}
```

A `Location` header points to `/api/rentals/{bookingNumber}`.

## POST /api/rentals/{bookingNumber}/return

Registers a return and calculates the final price.

### Request

```http
POST /api/rentals/TEST-001/return
Authorization: Bearer <access_token>
Content-Type: application/json
```

```json
{
  "returnTime": "2026-09-15T18:00:00Z",
  "returnOdometer": 10150,
  "baseDailyPrice": 500,
  "baseKmPrice": 2
}
```

### Fields

| Field | Type | Required | Rule |
|---|---|---:|---|
| `bookingNumber` | path string | Yes | Rental to return |
| `returnTime` | ISO-8601 timestamp | Yes | Cannot be before pickup |
| `returnOdometer` | integer | Yes | Cannot be below pickup odometer |
| `baseDailyPrice` | decimal | Yes | Must be >= 0 |
| `baseKmPrice` | decimal | Yes | Must be >= 0 |

### Success

**200 OK**

```json
{
  "bookingNumber": "TEST-001",
  "finalPrice": 1000
}
```

---

# 5. Error contract — all documented error codes

Every application-generated error has:

```json
{
  "errorCode": "PICKUP_INVALID_INPUT",
  "errorMessage": "The provided input was invalid."
}
```

**Always check the HTTP status first. Then use `errorCode` for programmatic handling. Never parse `errorMessage`.**

| Code | HTTP | Context | Meaning |
|---|---:|---|---|
| `AUTH_INVALID_INPUT` | 400 | `POST /oauth/token` | Authentication request input is missing or invalid. |
| `AUTH_INVALID_CREDENTIALS` | 401 | `POST /oauth/token` | Supplied credentials were rejected. |
| `AUTH_REQUIRED` | 401 | Protected endpoints | A valid bearer token is required. |
| `PICKUP_INVALID_INPUT` | 400 | Pickup | Pickup input cannot be accepted. |
| `PICKUP_BOOKING_ALREADY_EXISTS` | 400 | Pickup | Booking already exists for the authenticated tenant. |
| `RETURN_INVALID_INPUT` | 400 | Return | Return input or rental state cannot be accepted. |
| `RETURN_RENTAL_NOT_FOUND` | 404 | Return | No rental accessible to the authenticated tenant exists for the booking. |
| `INTERNAL_ERROR` | 500 | Any endpoint | Unexpected server-side failure. Details are not exposed to the customer. |

Unknown future error codes must be handled as an unknown error rather than causing the client to fail.

Framework-level failures, such as malformed JSON, may be rejected before an application handler creates an `errorCode`. For this reason the HTTP status code is always the first level of error handling.

A 404 on return is deliberately used both when the booking does not exist and when it belongs to another tenant. This prevents the API from disclosing another customer's data.

---

# 6. Persistence option A — PDF

PDF is the simplest persistence option.

When you select PDF:

1. We receive the pickup through the API.
2. We create a pickup PDF.
3. When the vehicle is returned, we create the return PDF.
4. The pickup document is retained.
5. The PDFs become the persisted rental record for your tenant.

The reference implementation uses the PDF itself as the document-oriented source of truth and stores a machine-readable rental snapshot in PDF metadata.

### What you need to provide

You do **not** need to create a database.

Before production we agree:

- where PDFs are stored (for example Azure Blob Storage or another approved storage target);
- retention period;
- backup/replication requirements;
- who owns the storage;
- who can access the documents.

The repository's local PDF implementation writes files to disk because it is a showcase. A production deployment should use the agreed durable storage target rather than relying on an application server's local filesystem.

---

# 7. Persistence option B — Azure SQL Database

Azure SQL Database is the relational-database reference option.

**Important:** the repository does not provision or connect to a real customer Azure SQL database. This section defines the database contract that a customer can provision. We then implement/configure the SQL Server persistence adapter for that tenant.

Azure SQL Database is a managed relational database service. Azure SQL uses firewall rules to control connectivity; Microsoft recommends restricting network access rather than broadly opening the database. See the [Microsoft Azure SQL security guidance](https://learn.microsoft.com/en-us/azure/azure-sql/database/secure-database-tutorial?view=azuresql).

## 7.1 Create the Azure SQL resource

Create:

- an Azure Resource Group;
- an Azure SQL logical server;
- an Azure SQL Database;
- a firewall/network configuration that permits the service to connect.

For production, prefer a restricted network path/private connectivity where appropriate. Do not enable broad access simply to make integration easier. Azure SQL's public endpoint is protected by firewall rules and uses port 1433.

Microsoft's current [Azure SQL Database quickstart](https://learn.microsoft.com/en-us/azure/azure-sql/database/single-database-create-quickstart?view=azuresql) is useful if you prefer the Azure Portal.

## 7.2 Azure CLI example

Replace the placeholders before running:

```bash
az login

RESOURCE_GROUP="car-rental-prod"
LOCATION="swedencentral"
SERVER_NAME="your-unique-sql-server-name"
DATABASE_NAME="CarRental"
ADMIN_USER="carRentalAdmin"
ADMIN_PASSWORD="<strong-password>"
YOUR_PUBLIC_IP="<your-public-ip>"

az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION"

az sql server create \
  --name "$SERVER_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --admin-user "$ADMIN_USER" \
  --admin-password "$ADMIN_PASSWORD"

az sql db create \
  --name "$DATABASE_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --server "$SERVER_NAME" \
  --edition GeneralPurpose \
  --compute-model Serverless \
  --family Gen5 \
  --capacity 2

az sql server firewall-rule create \
  --resource-group "$RESOURCE_GROUP" \
  --server "$SERVER_NAME" \
  --name "IntegrationClient" \
  --start-ip-address "$YOUR_PUBLIC_IP" \
  --end-ip-address "$YOUR_PUBLIC_IP"
```

The Azure CLI supports creating the server, database and firewall rule as separate resources.

**Do not put passwords into scripts committed to source control.** Use Azure Key Vault or another approved secret-management process.

## 7.3 Create the rental table

After connecting to the new database, run:

```sql
CREATE TABLE dbo.Rentals
(
    BookingNumber       nvarchar(100)  NOT NULL,
    TenantId            nvarchar(100)  NOT NULL,
    RegistrationNumber  nvarchar(50)   NOT NULL,
    CustomerIdentifier  nvarchar(200)  NOT NULL,
    Category            nvarchar(50)   NOT NULL,
    PickupTime          datetimeoffset NOT NULL,
    PickupOdometer      int            NOT NULL,
    ReturnTime          datetimeoffset NULL,
    ReturnOdometer      int            NULL,
    FinalPrice          decimal(18, 2) NULL,

    CONSTRAINT PK_Rentals
        PRIMARY KEY (TenantId, BookingNumber),

    CONSTRAINT CK_Rentals_PickupOdometer
        CHECK (PickupOdometer >= 0),

    CONSTRAINT CK_Rentals_ReturnOdometer
        CHECK (ReturnOdometer IS NULL OR ReturnOdometer >= PickupOdometer),

    CONSTRAINT CK_Rentals_Category
        CHECK (Category IN ('SmallCar', 'Combi', 'Truck'))
);
```

The composite primary key deliberately makes the booking unique **per tenant**, matching the API's tenant isolation model.

Add an index if your application frequently searches by vehicle:

```sql
CREATE INDEX IX_Rentals_RegistrationNumber
    ON dbo.Rentals (RegistrationNumber);
```

## 7.4 Give us the connection string

After the database and schema are ready, provide us with the connection string through the secure channel agreed during onboarding.

Example shape:

```text
Server=tcp:<server>.database.windows.net,1433;Initial Catalog=CarRental;User ID=<user>;Password=<secret>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

The actual password must be supplied through a secret-management mechanism, not committed to the repository.

Use an encrypted SQL connection and do not trust the server certificate in the connection string.

We then:

1. validate connectivity;
2. validate the schema;
3. configure your tenant to use the SQL persistence adapter;
4. run the persistence/integration tests;
5. perform a pickup/return smoke test.

The connection string is a **customer secret**. We do not need your database administrator credentials if you can provide a least-privileged application user.

---

# 8. Manual persistence integration

PDF and Azure SQL are reference choices, not a limitation.

If you already use another persistence technology, **we can perform a manual integration for you**.

Examples:

- PostgreSQL
- SQL Server
- MySQL
- Oracle
- an existing ERP
- an existing rental system
- REST/HTTP storage service
- S3-compatible object storage
- Azure Blob Storage
- another customer-specific database or API

The integration point is the persistence adapter:

```text
HTTP API
   ↓
RentalService
   ↓
IRentalStore
   ↓
Customer-specific adapter
   ↓
Customer technology
```

A manual integration normally includes:

1. agree the target data model or external API;
2. agree authentication/credentials;
3. implement the adapter;
4. map pickup and return operations;
5. implement error handling;
6. add persistence tests;
7. run integration/smoke tests;
8. configure the customer's tenant.

The API contract does not need to change just because the persistence technology changes.

---

# 9. Security requirements for customer integrations

Customers should:

- use HTTPS in production;
- never commit access tokens, passwords or connection strings;
- use least-privileged database credentials;
- restrict Azure SQL network access;
- use encrypted SQL connections;
- rotate credentials according to their security policy;
- treat booking numbers and customer identifiers as business data;
- handle unknown future error codes safely.

The API never returns database exceptions, stack traces or internal implementation details to customers.

---

# 10. Tests — verify the product yourself

The repository includes tests for the domain, pricing, application service, HTTP API, authentication, tenant isolation, persistence and observability.

### Full test suite

[CarRental.Tests — full test project](https://github.com/vallmar/activelogin-biluthyrning/tree/feature/customer-persistence-providers/tests/CarRental.Tests)

### API integration tests

[API integration tests](https://github.com/vallmar/activelogin-biluthyrning/blob/feature/customer-persistence-providers/tests/CarRental.Tests/ApiIntegrationTests.cs)

### Persistence tests

[Persistence provider tests](https://github.com/vallmar/activelogin-biluthyrning/blob/feature/customer-persistence-providers/tests/CarRental.Tests/PersistenceStoreTests.cs)

### Rental/application tests

[Rental service tests](https://github.com/vallmar/activelogin-biluthyrning/blob/feature/customer-persistence-providers/tests/CarRental.Tests/RentalServiceTests.cs)

### Domain tests

[Rental domain tests](https://github.com/vallmar/activelogin-biluthyrning/blob/feature/customer-persistence-providers/tests/CarRental.Tests/RentalTests.cs)

### Pricing tests

[Pricing tests](https://github.com/vallmar/activelogin-biluthyrning/blob/feature/customer-persistence-providers/tests/CarRental.Tests/PriceCalculatorTests.cs)

Run the suite locally with:

```bash
dotnet test
```

The tests are deliberately part of the product documentation: customers can inspect the actual automated verification instead of relying only on a written feature list.

---

# 11. What happens after onboarding

Once your persistence choice and credentials are ready, our implementation flow is:

```text
Customer chooses persistence
        ↓
Customer provisions target storage
        ↓
Customer provides connection/configuration securely
        ↓
We configure the tenant
        ↓
We implement the adapter if manual work is required
        ↓
Automated persistence tests
        ↓
Integration/smoke tests
        ↓
Production activation
```

For Azure SQL, you own the database and its Azure subscription unless otherwise agreed.

For PDF, we agree where the durable document storage lives and who owns it.

For manual integrations, we agree the integration scope before implementation.

---

# 12. Important production note

This repository is a reference implementation and integration specification.

The demo token issuer, local filesystem persistence and example credentials are intended to make the repository easy to inspect and test. They should not automatically be treated as production infrastructure.

A production deployment should use:

- a real OAuth 2.0/OIDC identity provider;
- durable storage;
- production secrets management;
- restricted network access;
- monitoring and alerting;
- backups/retention appropriate to the customer's requirements;
- a tested customer-specific persistence adapter where applicable.
