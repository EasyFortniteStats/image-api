namespace EasyFortniteStats_ImageApi.Middleware;

public class ApiKeyAuthenticationMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly string? _apiKey = configuration["API_KEY"] ?? Environment.GetEnvironmentVariable("API_KEY");

    public async Task InvokeAsync(HttpContext context)
    {
        // If no API key is configured, skip authentication
        if (string.IsNullOrEmpty(_apiKey))
        {
            await next(context);
            return;
        }

        // Check if Authorization header is present
        if (!context.Request.Headers.TryGetValue("Authorization", out var extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("API Key is missing");
            return;
        }

        // Validate the API key
        if (!_apiKey.Equals(extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized client");
            return;
        }

        await next(context);
    }
}
