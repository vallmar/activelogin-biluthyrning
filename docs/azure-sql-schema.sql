-- Car Rental API - Azure SQL persistence schema
-- This script is a CUSTOMER DATABASE CONTRACT.
-- The repository does not connect to or provision a real customer database.
-- Run this against the customer's Azure SQL Database.

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

CREATE INDEX IX_Rentals_RegistrationNumber
    ON dbo.Rentals (RegistrationNumber);
