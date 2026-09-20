using Microsoft.Data.SqlClient;
using CustomerB.AzureSql;
using CarRental.Contracts;

var connectionString = Environment.GetEnvironmentVariable("CUSTOMER_B_AZURE_SQL_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set CUSTOMER_B_AZURE_SQL_CONNECTION_STRING before running CustomerB.AzureSql.");
    Console.Error.WriteLine("Use the secure connection string supplied for the customer's Azure SQL database.");
    return;
}

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

const string createTable = """
IF OBJECT_ID(N'dbo.Rentals', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Rentals
    (
        BookingNumber       nvarchar(100)  NOT NULL,
        TenantId            nvarchar(100)  NOT NULL,
        RegistrationNumber  nvarchar(50)   NOT NULL,
        CustomerIdentifier  nvarchar(200)  NOT NULL,
        Category             nvarchar(50)   NOT NULL,
        PickupTime          datetimeoffset NOT NULL,
        PickupOdometer      int            NOT NULL,
        ReturnTime          datetimeoffset NULL,
        ReturnOdometer      int            NULL,
        FinalPrice           decimal(18, 2) NULL,
        CONSTRAINT PK_Rentals PRIMARY KEY (BookingNumber),
        CONSTRAINT CK_Rentals_PickupOdometer CHECK (PickupOdometer >= 0),
        CONSTRAINT CK_Rentals_ReturnOdometer CHECK (ReturnOdometer IS NULL OR ReturnOdometer >= PickupOdometer),
        CONSTRAINT CK_Rentals_Category CHECK (Category IN ('SmallCar', 'Combi', 'Truck'))
    );

    CREATE INDEX IX_Rentals_RegistrationNumber
        ON dbo.Rentals (RegistrationNumber);
END;
""";

await using (var command = new SqlCommand(createTable, connection))
    await command.ExecuteNonQueryAsync();

var apiUrl = Environment.GetEnvironmentVariable("RENTAL_API_URL") ?? "http://localhost:5000";
var clientId = Environment.GetEnvironmentVariable("RENTAL_API_CLIENT_ID") ?? "tenant-b";
var clientSecret = Environment.GetEnvironmentVariable("RENTAL_API_CLIENT_SECRET") ?? "secret-b";

using var api = new RentalApiClient(apiUrl, clientId, clientSecret);

var pickup = await api.RegisterPickupAsync(
    "XYZ789",
    "customer-b-1",
    ContractCarCategory.Truck,
    DateTimeOffset.UtcNow,
    25_000);

Console.WriteLine($"SaaS pickup created: {pickup.BookingNumber}");
Console.WriteLine("The Azure SQL schema is customer-owned. The generated booking number is returned by the SaaS API.");
