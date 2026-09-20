using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using CarRental.Application.Ports;
using CarRental.Application.Pricing;
using CarRental.Application.Rentals;
using CarRental.Contracts;
using CarRental.Domain;
using CarRental.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

if (Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");

var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey));

builder.Logging.AddProvider(new DailyFileLoggerProvider(
    Path.Combine(builder.Environment.ContentRootPath, ".log")));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = securityKey,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<IBookingNumberRegistry>(sp =>
    new FileBookingNumberRegistry(
        Path.Combine(
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
            sp.GetRequiredService<IConfiguration>()["Persistence:RootDirectory"] ?? "data",
            "booking-numbers.json")));

builder.Services.AddSingleton<IRentalStoreResolver>(sp =>
    new ConfiguredRentalStoreResolver(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<IHostEnvironment>().ContentRootPath));
builder.Services.AddScoped<RentalService>();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        var exception = exceptionFeature?.Error;
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

        if (exception is not null)
        {
            logger.LogError(
                exception,
                "Unhandled exception while processing {HttpMethod} {Path}",
                context.Request.Method,
                context.Request.Path);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Json(new ErrorResponse(
            ErrorCodes.InternalError,
            Program.GetErrorMessage(ErrorCodes.InternalError))).ExecuteAsync(context);
    });
});

app.Use(async (context, next) =>
{
    await next();

    if (context.Response.StatusCode is >= 400 and < 500)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var tenantId = context.User.FindFirst("client_id")?.Value ?? "anonymous";

        logger.LogWarning(
            "HTTP request failed with {StatusCode}. Tenant {TenantId}, {HttpMethod} {Path}",
            context.Response.StatusCode,
            tenantId,
            context.Request.Method,
            context.Request.Path);
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/oauth/token", (TokenRequest? request) =>
{
    if (request is null ||
        string.IsNullOrWhiteSpace(request.ClientId) ||
        string.IsNullOrWhiteSpace(request.ClientSecret))
    {
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.AuthenticationInvalidInput,
            Program.GetErrorMessage(ErrorCodes.AuthenticationInvalidInput)));
    }

    if (!DemoClients.TryGetValue(request.ClientId, out var client) || client.ClientSecret != request.ClientSecret)
    {
        return Results.Json(
            new ErrorResponse(
                ErrorCodes.AuthenticationInvalidCredentials,
                Program.GetErrorMessage(ErrorCodes.AuthenticationInvalidCredentials)),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var now = DateTime.UtcNow;
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims:
        [
            new Claim(JwtRegisteredClaimNames.Sub, request.ClientId),
            new Claim("client_id", request.ClientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        ],
        notBefore: now,
        expires: now.AddHours(1),
        signingCredentials: new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256));

    return Results.Ok(new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", 3600));
}).AllowAnonymous();

app.MapPost("/api/rentals/pickup", async (
    RegisterPickupRequest? request,
    HttpContext context,
    RentalService service,
    CancellationToken ct) =>
{
    if (request is null || !IsValidPickupRequest(request))
    {
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.PickupInvalidInput,
            Program.GetErrorMessage(ErrorCodes.PickupInvalidInput)));
    }

    var tenantId = GetTenantId(context);

    try
    {
        var rental = await service.RegisterPickupAsync(
            tenantId,
            request.BookingNumber,
            request.RegistrationNumber,
            request.CustomerIdentifier,
            ToDomainCategory(request.Category),
            request.PickupTime,
            request.PickupOdometer,
            ct);

        var response = new RegisterPickupResponse(
            rental.BookingNumber,
            rental.RegistrationNumber,
            rental.CustomerIdentifier,
            ToContractCategory(rental.Category),
            rental.PickupTime,
            rental.PickupOdometer,
            rental.IsReturned);

        return Results.Created($"/api/rentals/{rental.BookingNumber}", response);
    }
    catch (InvalidOperationException ex)
    {
        app.Logger.LogWarning(ex, "Pickup request rejected for {HttpMethod} {Path}", "POST", "/api/rentals/pickup");
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.PickupBookingAlreadyExists,
            Program.GetErrorMessage(ErrorCodes.PickupBookingAlreadyExists)));
    }
    catch (ArgumentException ex)
    {
        app.Logger.LogWarning(ex, "Invalid pickup request input for {HttpMethod} {Path}", "POST", "/api/rentals/pickup");
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.PickupInvalidInput,
            Program.GetErrorMessage(ErrorCodes.PickupInvalidInput)));
    }
}).RequireAuthorization();

app.MapPost("/api/rentals/{bookingNumber}/return", async (
    string bookingNumber,
    RegisterReturnRequest? request,
    HttpContext context,
    RentalService service,
    CancellationToken ct) =>
{
    if (request is null || !IsValidReturnRequest(bookingNumber, request))
    {
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.ReturnInvalidInput,
            Program.GetErrorMessage(ErrorCodes.ReturnInvalidInput)));
    }

    var tenantId = GetTenantId(context);

    try
    {
        var price = await service.RegisterReturnAsync(
            tenantId,
            bookingNumber,
            request.ReturnTime,
            request.ReturnOdometer,
            new Pricing(request.BaseDailyPrice, request.BaseKmPrice),
            ct);

        return Results.Ok(new RegisterReturnResponse(bookingNumber, price));
    }
    catch (KeyNotFoundException ex)
    {
        app.Logger.LogWarning(ex, "Rental not found for return request {HttpMethod} {Path}", "POST", $"/api/rentals/{bookingNumber}/return");
        return Results.NotFound(new ErrorResponse(
            ErrorCodes.ReturnRentalNotFound,
            Program.GetErrorMessage(ErrorCodes.ReturnRentalNotFound)));
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        app.Logger.LogWarning(ex, "Invalid return request input for {HttpMethod} {Path}", "POST", $"/api/rentals/{bookingNumber}/return");
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.ReturnInvalidInput,
            Program.GetErrorMessage(ErrorCodes.ReturnInvalidInput)));
    }
}).RequireAuthorization();

#if DEBUG
app.MapGet("/api/test/unhandled-error", () => throw new InvalidOperationException("Intentional test exception."))
    .RequireAuthorization();
#endif

app.Run();

public partial class Program
{
    internal static string GetTenantId(HttpContext context)
        => context.User.FindFirst("client_id")?.Value
           ?? throw new UnauthorizedAccessException("Tenant identity is missing.");

    static CarCategory ToDomainCategory(ContractCarCategory category) => category switch
    {
        ContractCarCategory.SmallCar => CarCategory.SmallCar,
        ContractCarCategory.Combi => CarCategory.Combi,
        ContractCarCategory.Truck => CarCategory.Truck,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown car category.")
    };

    static ContractCarCategory ToContractCategory(CarCategory category) => category switch
    {
        CarCategory.SmallCar => ContractCarCategory.SmallCar,
        CarCategory.Combi => ContractCarCategory.Combi,
        CarCategory.Truck => ContractCarCategory.Truck,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown car category.")
    };

    private static bool IsValidPickupRequest(RegisterPickupRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.BookingNumber) &&
               !string.IsNullOrWhiteSpace(request.RegistrationNumber) &&
               !string.IsNullOrWhiteSpace(request.CustomerIdentifier) &&
               Enum.IsDefined(request.Category) &&
               request.PickupTime != default &&
               request.PickupOdometer >= 0;
    }

    private static bool IsValidReturnRequest(string bookingNumber, RegisterReturnRequest request)
    {
        return !string.IsNullOrWhiteSpace(bookingNumber) &&
               request.ReturnTime != default &&
               request.ReturnOdometer >= 0 &&
               request.BaseDailyPrice >= 0 &&
               request.BaseKmPrice >= 0;
    }

    private static readonly IReadOnlyDictionary<string, DemoClient> DemoClients =
        new Dictionary<string, DemoClient>(StringComparer.Ordinal)
        {
            ["tenant-a"] = new DemoClient("secret-a"),
            ["tenant-b"] = new DemoClient("secret-b")
        };

    private static readonly IReadOnlyDictionary<string, string> ErrorMessages =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ErrorCodes.AuthenticationInvalidInput] = "The provided input was invalid.",
            [ErrorCodes.AuthenticationInvalidCredentials] = "The supplied credentials were invalid.",
            [ErrorCodes.AuthenticationRequired] = "A valid tenant access token is required.",
            [ErrorCodes.PickupInvalidInput] = "The provided input was invalid.",
            [ErrorCodes.PickupBookingAlreadyExists] = "The provided input could not be processed.",
            [ErrorCodes.ReturnInvalidInput] = "The provided input was invalid.",
            [ErrorCodes.ReturnRentalNotFound] = "The provided input could not be processed.",
            [ErrorCodes.InternalError] = "An unexpected error occurred."
        };

    internal static string GetErrorMessage(string errorCode) => ErrorMessages[errorCode];
}

internal sealed record DemoClient(string ClientSecret);
