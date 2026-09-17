using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarRental.Contracts;

namespace CustomerB.Postgres;

public sealed class RentalApiClient
{
    private static readonly JsonSerializerOptions CustomerJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient client;
    private readonly string clientId;
    private readonly string clientSecret;

    public RentalApiClient(string baseUrl, string clientId, string clientSecret)
    {
        client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        this.clientId = clientId;
        this.clientSecret = clientSecret;
    }

    public async Task<RegisterPickupResponse> RegisterPickupAsync(
        CustomerRental rental,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rentals/pickup")
        {
            Content = JsonContent.Create(
                new RegisterPickupRequest(
                    rental.BookingNumber,
                    rental.RegistrationNumber,
                    rental.CustomerId,
                    rental.Category,
                    rental.PickupTime,
                    rental.PickupOdometer),
                options: CustomerJsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(CustomerJsonOptions, cancellationToken);
            throw new InvalidOperationException(
                $"Rental API error {error?.ErrorCode ?? "UNKNOWN_ERROR"}: {error?.ErrorMessage ?? "The rental service could not process the request."}");
        }

        return await response.Content.ReadFromJsonAsync<RegisterPickupResponse>(CustomerJsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The rental service returned an empty pickup response.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/oauth/token",
            new { clientId, clientSecret },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(CustomerJsonOptions, cancellationToken);
            throw new InvalidOperationException(
                $"Rental API authentication error {error?.ErrorCode ?? "UNKNOWN_ERROR"}: {error?.ErrorMessage ?? "The authentication service rejected the customer credentials."}");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The authentication service returned an empty token response.");

        return token.AccessToken;
    }

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
}

public sealed record CustomerRental(
    string BookingNumber,
    string RegistrationNumber,
    string CustomerId,
    ContractCarCategory Category,
    DateTimeOffset PickupTime,
    int PickupOdometer);
