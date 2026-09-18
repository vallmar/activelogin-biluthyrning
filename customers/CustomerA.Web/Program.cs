using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var apiUrl = builder.Configuration["RentalApiUrl"] ?? "http://localhost:5000";
var clientId = builder.Configuration["RentalApiClientId"] ?? "tenant-a";
var clientSecret = builder.Configuration["RentalApiClientSecret"] ?? "secret-a";

builder.Services.AddHttpClient("RentalApi", client => client.BaseAddress = new Uri(apiUrl));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/send", async (SendRequest request, IHttpClientFactory factory, CancellationToken ct) =>
{
    if (request.Method is not ("pickup" or "return"))
        return Results.BadRequest(new { error = "Method must be pickup or return." });

    try
    {
        using var json = JsonDocument.Parse(request.Json);
        var client = factory.CreateClient("RentalApi");

        using var tokenResponse = await client.PostAsJsonAsync(
            "/oauth/token",
            new { clientId, clientSecret },
            ct);

        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(ct);

        if (!tokenResponse.IsSuccessStatusCode)
            return Results.Ok(new SendResponse((int)tokenResponse.StatusCode, "/oauth/token", null, TryParse(tokenBody)));

        var token = JsonSerializer.Deserialize<TokenResponse>(tokenBody);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
            return Results.Ok(new SendResponse(500, "/oauth/token", null, TryParse(tokenBody)));

        var path = request.Method == "pickup"
            ? "/api/rentals/pickup"
            : $"/api/rentals/{Uri.EscapeDataString(request.BookingNumber ?? "")}/return";

        using var apiRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json.RootElement.GetRawText(), Encoding.UTF8, "application/json")
        };
        apiRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        using var response = await client.SendAsync(apiRequest, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        return Results.Ok(new SendResponse(
            (int)response.StatusCode,
            path,
            json.RootElement,
            TryParse(responseText)));
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Request JSON is not valid JSON." });
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
