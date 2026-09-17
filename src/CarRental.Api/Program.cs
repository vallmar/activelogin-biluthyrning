using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using CarRental.Application.Ports;
using CarRental.Application.Pricing;
using CarRental.Application.Rentals;
using CarRental.Contracts;
using CarRental.Domain;
using CarRental.Infrastructure.InMemory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
builder.Services.AddSingleton<IRentalRepository, InMemoryRentalRepository>();
builder.Services.AddScoped<ApiTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ApiTenantContext>());
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
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();

app.MapPost("/oauth/token", (TokenRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.ClientId) ||
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

app.MapPost("/api/rentals/pickup", async (RegisterPickupRequest request, RentalService service, CancellationToken ct) =>
{
    if (!IsValidPickupRequest(request))
    {
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.PickupInvalidInput,
            Program.GetErrorMessage(ErrorCodes.PickupInvalidInput)));
    }

    try
    {
        var rental = await service.RegisterPickupAsync(
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
        var logger = app.Logger;
        logger.LogWarning(ex, "Pickup request rejected for {HttpMethod} {Path}", "POST", "/api/rentals/pickup");
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.PickupBookingAlreadyExists,
            Program.GetErrorMessage(ErrorCodes.PickupBookingAlreadyExists)));
    }
    catch (ArgumentException ex)
    {
        var logger = app.Logger;
        logger.LogWarning(ex, "Invalid pickup request input for {HttpMethod} {Path}", "POST", "/api/rentals/pickup");
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.PickupInvalidInput,
            Program.GetErrorMessage(ErrorCodes.PickupInvalidInput)));
    }
}).RequireAuthorization();

app.MapPost("/api/rentals/{bookingNumber}/return", async (string bookingNumber, RegisterReturnRequest request, RentalService service, CancellationToken ct) =>
{
    if (!IsValidReturnRequest(bookingNumber, request))
    {
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.ReturnInvalidInput,
            Program.GetErrorMessage(ErrorCodes.ReturnInvalidInput)));
    }

    try
    {
        var price = await service.RegisterReturnAsync(
            bookingNumber,
            request.ReturnTime,
            request.ReturnOdometer,
            new Pricing(request.BaseDailyPrice, request.BaseKmPrice),
            ct);

        return Results.Ok(new RegisterReturnResponse(bookingNumber, price));
    }
    catch (KeyNotFoundException ex)
    {
        var logger = app.Logger;
        logger.LogWarning(ex, "Rental not found for return request {HttpMethod} {Path}", "POST", $"/api/rentals/{bookingNumber}/return");
        return Results.NotFound(new ErrorResponse(
            ErrorCodes.ReturnRentalNotFound,
            Program.GetErrorMessage(ErrorCodes.ReturnRentalNotFound)));
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        var logger = app.Logger;
        logger.LogWarning(ex, "Invalid return request input for {HttpMethod} {Path}", "POST", $"/api/rentals/{bookingNumber}/return");
        return Results.BadRequest(new ErrorResponse(
            ErrorCodes.ReturnInvalidInput,
            Program.GetErrorMessage(ErrorCodes.ReturnInvalidInput)));
    }
}).RequireAuthorization();

#if DEBUG
app.MapGet("/api/test/unhandled-error", (HttpContext context) => throw new InvalidOperationException("Intentional test exception."))
    .RequireAuthorization();
#endif

app.Run();

public partial class Program
{
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
        ContractCarCategory.Combi => ContractCarCategory.Combi,
        ContractCarCategory.Truck => ContractCarCategory.Truck,
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

    private static readonly IReadOnlyDictionary<string, DemoClient> DemoClients = new Dictionary<string, DemoClient>(StringComparer.Ordinal)
    {
        ["tenant-a"] = new DemoClient("secret-a"),
        ["tenant-b"] = new DemoClient("secret-b")
    };

    private static readonly IReadOnlyDictionary<string, string> ErrorMessages = new Dictionary<string, string>(StringComparer.Ordinal)
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

public sealed class ApiTenantContext : ITenantContext
{
    public string TenantId { get; private set; } = string.Empty;

    public void SetTenant(string tenantId) => TenantId = tenantId;
}

internal sealed record DemoClient(string ClientSecret);

file sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ApiTenantContext tenantContext)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await UnauthorizedAsync(context);
            return;
        }

        var tenantId = context.User.FindFirst("client_id")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await UnauthorizedAsync(context);
            return;
        }

        tenantContext.SetTenant(tenantId);
        await next(context);
    }

    private static async Task UnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await Results.Json(new ErrorResponse(
            ErrorCodes.AuthenticationRequired,
            Program.GetErrorMessage(ErrorCodes.AuthenticationRequired))).ExecuteAsync(context);
    }
}
