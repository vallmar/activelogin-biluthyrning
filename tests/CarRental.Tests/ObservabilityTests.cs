using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CarRental.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CarRental.Tests;

public sealed class ObservabilityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ObservabilityTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Unexpected_exception_returns_sanitized_500_and_is_logged()
    {
        var logSink = new TestLogSink();
        var client = CreateLoggingClient(logSink);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test/unhandled-error");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(client, "tenant-a"));
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(ErrorCodes.InternalError, body!.ErrorCode);
        Assert.Equal("An unexpected error occurred.", body.ErrorMessage);
        Assert.DoesNotContain("Intentional test exception", body.ErrorMessage);
        Assert.DoesNotContain("InvalidOperationException", body.ErrorMessage);

        Assert.Contains(
            logSink.Entries,
            entry => entry.LogLevel == LogLevel.Error
                      && entry.Message.Contains("Unhandled exception while processing GET /api/test/unhandled-error"));

        Assert.Contains(
            logSink.Entries,
            entry => entry.Exception?.Message == "Intentional test exception.");
    }

    [Fact]
    public async Task Rejected_unauthorized_request_is_logged_as_warning()
    {
        var logSink = new TestLogSink();
        var client = CreateLoggingClient(logSink);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rentals/pickup")
        {
            Content = JsonContent.Create(new
            {
                bookingNumber = "UNAUTHORIZED-TEST",
                registrationNumber = "ABC123",
                customerIdentifier = "customer-a",
                category = "SmallCar",
                pickupTime = "2026-09-15T10:00:00Z",
                pickupOdometer = 10000
            })
        };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.AuthenticationRequired, error!.ErrorCode);
        Assert.Equal("A valid tenant access token is required.", error.ErrorMessage);

        Assert.Contains(
            logSink.Entries,
            entry => entry.LogLevel == LogLevel.Warning
                      && entry.Message.Contains("HTTP request failed with 401")
                      && entry.Message.Contains("Tenant anonymous")
                      && entry.Message.Contains("POST /api/rentals/pickup"));
    }

    [Fact]
    public async Task Cross_tenant_access_is_blocked_and_logged_as_warning()
    {
        var logSink = new TestLogSink();
        var client = CreateLoggingClient(logSink);
        var bookingNumber = $"SECURITY-{Guid.NewGuid():N}";
        var pickup = new RegisterPickupRequest(
            bookingNumber,
            "ABC123",
            "customer-a",
            ContractCarCategory.SmallCar,
            DateTimeOffset.Parse("2026-09-15T10:00:00Z"),
            10000);

        using (var pickupRequest = new HttpRequestMessage(HttpMethod.Post, "/api/rentals/pickup")
        {
            Content = JsonContent.Create(pickup)
        })
        {
            pickupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(client, "tenant-a"));
            var pickupResponse = await client.SendAsync(pickupRequest, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, pickupResponse.StatusCode);
        }

        var returnRequest = new RegisterReturnRequest(
            DateTimeOffset.Parse("2026-09-15T18:00:00Z"),
            10100,
            500m,
            2m);

        using var unauthorizedRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/rentals/{bookingNumber}/return")
        {
            Content = JsonContent.Create(returnRequest)
        };
        unauthorizedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(client, "tenant-b"));

        var response = await client.SendAsync(unauthorizedRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.ReturnRentalNotFound, error!.ErrorCode);
        Assert.Equal("The provided input could not be processed.", error.ErrorMessage);


    }

    private HttpClient CreateLoggingClient(TestLogSink logSink)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new TestLoggerProvider(logSink));
            });
        }).CreateClient();

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string tenantId)
    {
        using var response = await client.PostAsJsonAsync(
            "/oauth/token",
            new { clientId = tenantId, clientSecret = $"secret-{tenantId[^1]}" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(token);
        return token!.AccessToken;
    }

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

    private sealed class TestLogSink
    {
        public List<LogEntry> Entries { get; } = [];
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);

    private sealed class TestLoggerProvider(TestLogSink sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TestLogger(sink);

        public void Dispose()
        {
        }
    }

    private sealed class TestLogger(TestLogSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            sink.Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
