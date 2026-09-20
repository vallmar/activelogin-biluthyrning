using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarRental.Contracts;

namespace CustomerB.AzureSql;

public sealed class RentalApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
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
        string registrationNumber,
        string customerIdentifier,
        ContractCarCategory category,
        DateTimeOffset pickupTime,
        int pickupOdometer,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rentals/pickup")
        {
            Content = JsonContent.Create(
                new RegisterPickupRequest(
                    registrationNumber,
                    customerIdentifier,
                    category,
                    pickupTime,
                    pickupOdometer),
                options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RegisterPickupResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The rental API returned an empty pickup response.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/oauth/token",
            new { clientId, clientSecret },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The authentication service returned an empty token response.");

        return token.AccessToken;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions, cancellationToken);
        throw new InvalidOperationException(
            $"Rental API error {error?.ErrorCode ?? "UNKNOWN_ERROR"}: {error?.ErrorMessage ?? "The request failed."}");
    }

    public void Dispose() => client.Dispose();

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
}
