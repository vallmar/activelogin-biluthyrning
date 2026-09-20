using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var apiUrl = builder.Configuration["RentalApiUrl"]
    ?? Environment.GetEnvironmentVariable("RENTAL_API_URL")
    ?? "http://localhost:5000";
var clientId = builder.Configuration["RentalApiClientId"]
    ?? Environment.GetEnvironmentVariable("RENTAL_API_CLIENT_ID")
    ?? "tenant-a";
var clientSecret = builder.Configuration["RentalApiClientSecret"]
    ?? Environment.GetEnvironmentVariable("RENTAL_API_CLIENT_SECRET")
    ?? "secret-a";

builder.Services.AddHttpClient("RentalApi", client =>
{
    client.BaseAddress = new Uri(apiUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/send", async (
    SendRequest request,
    IHttpClientFactory factory,
    CancellationToken ct) =>
{
    if (request.Method is not ("pickup" or "return"))
        return Results.BadRequest(new { error = "Method must be pickup or return." });

    if (request.Method == "return" && string.IsNullOrWhiteSpace(request.BookingNumber))
        return Results.BadRequest(new { error = "A booking number is required for a return." });

    JsonDocument json;
    try
    {
        json = JsonDocument.Parse(request.Json);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Request JSON is not valid JSON." });
    }

    using (json)
    {
        try
        {
            var client = factory.CreateClient("RentalApi");

            using var tokenResponse = await client.PostAsJsonAsync(
                "/oauth/token",
                new { clientId, clientSecret },
                ct);

            var tokenBody = await tokenResponse.Content.ReadAsStringAsync(ct);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                return Results.Json(
                    new SendResponse(
                        (int)tokenResponse.StatusCode,
                        "/oauth/token",
                        null,
                        TryParse(tokenBody)),
                    statusCode: (int)tokenResponse.StatusCode);
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(tokenBody, jsonOptions);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                return Results.Problem(\n                    detail: "The rental API returned a successful token response, but the access token could not be read.",\n                    statusCode: StatusCodes.Status502BadGateway);
            }

            var path = request.Method == "pickup"
                ? "/api/rentals/pickup"
                : $"/api/rentals/{Uri.EscapeDataString(request.BookingNumber!)}/return";

            using var apiRequest = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(
                    json.RootElement.GetRawText(),
                    Encoding.UTF8,
                    "application/json")
            };
            apiRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);

            using var response = await client.SendAsync(apiRequest, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            return Results.Json(
                new SendResponse(
                    (int)response.StatusCode,
                    path,
                    json.RootElement,
                    TryParse(responseText)),
                statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                detail: $"The rental API could not be reached: {ex.Message}",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Problem(
                detail: "The rental API request timed out.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }
});

app.MapFallbackToFile("index.html");
app.Run();

static object TryParse(string text)
{
    try { return JsonSerializer.Deserialize<JsonElement>(text); }
    catch { return text; }
}

public sealed record SendRequest(string Method, string? BookingNumber, string Json);
public sealed record SendResponse(int StatusCode, string Url, object? RequestJson, object ResponseJson);
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
