# Customer integration examples

These projects intentionally represent customers rather than parts of the SaaS core.

Both examples integrate through the public HTTP API and obtain a JWT access token from the showcase `/oauth/token` endpoint before calling rental endpoints.

## Customer A — PDF

`CustomerA.PdfConsole` demonstrates the PDF persistence reference choice.

The customer application:
- calls the public HTTP API only;
- never supplies a booking number during pickup;
- reads the generated booking number from the pickup response;
- uses that booking number for the return call.

The rental records themselves are persisted by the SaaS PDF persistence adapter configured for the tenant.

`CustomerA.Web` is a browser-based request tester for the same customer integration. It uses the real API, shows editable request templates, captures the generated booking number after pickup, and uses that booking number for return.

## Customer B — Azure SQL Database (not in here)

`CustomerB.AzureSql` demonstrates the Azure SQL customer integration contract.

The customer:
- owns the Azure SQL database;
- supplies the connection string through `CUSTOMER_B_AZURE_SQL_CONNECTION_STRING`;
- creates the documented `dbo.Rentals` schema;
- calls the same public HTTP API.

The repository's Azure SQL implementation is a reference/demo integration and does not connect to a real customer database.

Neither customer project references `CarRental.Domain` or `CarRental.Application`.
