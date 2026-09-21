using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarRental.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CarRental.Tests;

public sealed class ApiIntegrationTests : IClassFixture<ApiTestFactory>
{
    // API tests stay at the public HTTP boundary. Shared helpers only prepare requests/authentication; private endpoint methods are not tested directly.
    private static readonly JsonSerializerOptions CustomerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient client;

    public ApiIntegrationTests(ApiTestFactory factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Pickup_requires_tenant_identity()
    {
        var request = new RegisterPickupRequest("ABC123", "customer-a", ContractCarCategory.SmallCar, DateTimeOffset.Parse("2026-09-15T10:00:00Z"), 10000);

        using var content = JsonContent.Create(request, options: CustomerJsonOptions);
        var response = await client.PostAsync("/api/rentals/pickup", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));

        var error = await ReadCustomerJsonAsync<ErrorResponse>(response);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.AuthenticationRequired, error!.ErrorCode);
        Assert.Equal("A valid tenant access token is required.", error.ErrorMessage);
    }

    [Fact]
    public async Task Invalid_access_token_is_rejected_with_customer_error_contract()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test/unhandled-error");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));

        var error = await ReadCustomerJsonAsync<ErrorResponse>(response);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.AuthenticationRequired, error!.ErrorCode);
        Assert.Equal("A valid tenant access token is required.", error.ErrorMessage);
    }

    [Fact]
    public async Task Pickup_returns_201_and_customer_response_contract()
    {
        var request = new RegisterPickupRequest("ABC123", "customer-a", ContractCarCategory.SmallCar, DateTimeOffset.Parse("2026-09-15T10:00:00Z"), 10000);
        var response = await PostAsCustomerJsonAsync("/api/rentals/pickup", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadCustomerJsonAsync<RegisterPickupResponse>(response);
        Assert.NotNull(body);
        var bookingNumber = body!.BookingNumber;
        Assert.False(string.IsNullOrWhiteSpace(bookingNumber));
        Assert.StartsWith("R-", bookingNumber);
        Assert.Equal("ABC123", body.RegistrationNumber);
        Assert.Equal("customer-a", body.CustomerIdentifier);
        Assert.Equal(ContractCarCategory.SmallCar, body.Category);
        Assert.Equal(10000, body.PickupOdometer);
        Assert.False(body.IsReturned);
        Assert.Equal($"/api/rentals/{bookingNumber}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Tenant_cannot_return_another_tenants_rental()
    {
        var bookingNumber = await RegisterPickupAsync(ContractCarCategory.Combi, 10000, "tenant-a");

        var request = new RegisterReturnRequest(DateTimeOffset.Parse("2026-09-15T18:00:00Z"), 10100, 500m, 2m);
        var response = await PostAsCustomerJsonAsync($"/api/rentals/{bookingNumber}/return", request, "tenant-b");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await ReadCustomerJsonAsync<ErrorResponse>(response);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.ReturnRentalNotFound, error!.ErrorCode);
        Assert.Equal("The provided input could not be processed.", error.ErrorMessage);
    }

    [Fact]
    public async Task Return_returns_200_and_final_price()
    {
        var bookingNumber = await RegisterPickupAsync(ContractCarCategory.Combi, 10000);
        var request = new RegisterReturnRequest(DateTimeOffset.Parse("2026-09-15T18:00:00Z"), 10100, 500m, 2m);
        var response = await PostAsCustomerJsonAsync($"/api/rentals/{bookingNumber}/return", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadCustomerJsonAsync<RegisterReturnResponse>(response);
        Assert.NotNull(body);
        Assert.Equal(bookingNumber, body!.BookingNumber);
        Assert.Equal(850m, body.FinalPrice);
    }

    [Fact]
    public async Task Return_returns_404_with_customer_error_code_when_rental_does_not_exist()
    {
        var bookingNumber = $"UNKNOWN-{Guid.NewGuid():N}";
        var request = new RegisterReturnRequest(DateTimeOffset.Parse("2026-09-15T18:00:00Z"), 10100, 500m, 2m);
        var response = await PostAsCustomerJsonAsync($"/api/rentals/{bookingNumber}/return", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await ReadCustomerJsonAsync<ErrorResponse>(response);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.ReturnRentalNotFound, error!.ErrorCode);
        Assert.Equal("The provided input could not be processed.", error.ErrorMessage);
    }

    [Fact]
    public async Task Return_returns_400_with_customer_error_code_when_rental_has_already_been_returned()
    {
        var bookingNumber = await RegisterPickupAsync(ContractCarCategory.SmallCar, 10000);
        var request = new RegisterReturnRequest(DateTimeOffset.Parse("2026-09-15T18:00:00Z"), 10100, 500m, 2m);
        var firstResponse = await PostAsCustomerJsonAsync($"/api/rentals/{bookingNumber}/return", request);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await PostAsCustomerJsonAsync($"/api/rentals/{bookingNumber}/return", request);
        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
        var error = await ReadCustomerJsonAsync<ErrorResponse>(secondResponse);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.ReturnInvalidInput, error!.ErrorCode);
        Assert.Equal("Rental has already been returned.", error.ErrorMessage);
    }

    [Fact]
    public async Task Return_returns_400_with_customer_error_code_when_return_time_is_before_pickup()
    {
        var bookingNumber = await RegisterPickupAsync(ContractCarCategory.SmallCar, 10000);
        var request = new RegisterReturnRequest(DateTimeOffset.Parse("2026-09-15T09:00:00Z"), 10100, 500m, 2m);
        var response = await PostAsCustomerJsonAsync($"/api/rentals/{bookingNumber}/return", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadCustomerJsonAsync<ErrorResponse>(response);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.ReturnInvalidInput, error!.ErrorCode);
        Assert.Equal("The provided input was invalid.", error.ErrorMessage);
    }

    [Fact]
    public async Task Return_returns_400_with_customer_error_code_when_return_odometer_is_lower_than_pickup()
    {
        var bookingNumber = await RegisterPickupAsync(ContractCarCategory.SmallCar, 10000);
        var request = new RegisterReturnRequest(DateTimeOffset.Parse("2026-09-15T18:00:00Z"), 9999, 500m, 2m);
        var response = await PostAsCustomerJsonAsync($"/api/rentals/{bookingNumber}/return", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadCustomerJsonAsync<ErrorResponse>(response);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.ReturnInvalidInput, error.ErrorCode);
        Assert.Equal("The provided input was invalid.", error.ErrorMessage);
    }

    private async Task<string> RegisterPickupAsync(ContractCarCategory category, int odometer, string tenantId = "tenant-a")
    {
        var request = new RegisterPickupRequest("ABC123", "customer-a", category, DateTimeOffset.Parse("2026-09-15T10:00:00Z"), odometer);
        var response = await PostAsCustomerJsonAsync("/api/rentals/pickup", request, tenantId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadCustomerJsonAsync<RegisterPickupResponse>(response);
        Assert.NotNull(body);
        return body!.BookingNumber;
    }

    private async Task<HttpResponseMessage> PostAsCustomerJsonAsync<T>(string uri, T value, string tenantId = "tenant-a")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(value, options: CustomerJsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(tenantId));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(string tenantId)
    {
        using var response = await client.PostAsJsonAsync(
            "/oauth/token",
            new { clientId = tenantId, clientSecret = $"secret-{tenantId[^1]}" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(token);
        Assert.Equal("Bearer", token!.TokenType);
        return token.AccessToken;
    }

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

    private static Task<T?> ReadCustomerJsonAsync<T>(HttpResponseMessage response)
        => response.Content.ReadFromJsonAsync<T>(CustomerJsonOptions);

}


public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "CarRentalApiTests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:RootDirectory"] = dataDirectory
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(dataDirectory))
            Directory.Delete(dataDirectory, recursive: true);
    }
}
