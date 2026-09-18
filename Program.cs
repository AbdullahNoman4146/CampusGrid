using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using CampusGrid.Configuration;
using CampusGrid.LLM;
using CampusGrid.Models.Common;
using CampusGrid.Optimization;
using CampusGrid.Services;
using CampusGrid.Validation;

var builder = WebApplication.CreateBuilder(args);

// Support hosts such as Render that provide a generic PORT variable.
var hostPort = Environment.GetEnvironmentVariable("PORT");
if (int.TryParse(hostPort, out var parsedPort) && parsedPort is > 0 and <= 65535 &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{parsedPort}");
}

// 1. Configure JSON serialization with snake_case naming
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });

// 2. Custom 400 Bad Request response for model binding / deserialization errors
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => !string.IsNullOrWhiteSpace(e.ErrorMessage) ? e.ErrorMessage : e.Exception?.Message ?? "Invalid format")
            .ToList();

        var errorResponse = new ApiErrorResponse
        {
            Error = "Bad Request",
            Message = "Invalid request payload format or schema.",
            Details = errors,
            StatusCode = StatusCodes.Status400BadRequest
        };

        return new BadRequestObjectResult(errorResponse);
    };
});

// 3. Swagger / OpenAPI Documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "GridWise Smart Campus Energy Optimization API",
        Version = "v1",
        Description = "Production AI-powered HTTP API service for BUP CSE Fest 2026 Hackathon."
    });
});

// 4. Configuration Options
builder.Services.Configure<LLMOptions>(builder.Configuration.GetSection(LLMOptions.SectionName));

// 5. HTTP Client & LLM Services
builder.Services.AddHttpClient<LLMInterpreterService>();
builder.Services.AddScoped<ILLMInterpreter, LLMInterpreterService>();

// 6. Validation Services
builder.Services.AddSingleton<IDirectiveValidator, DirectiveValidator>();
builder.Services.AddSingleton<IEnergyRequestValidator, EnergyRequestValidator>();
builder.Services.AddSingleton<IScheduleValidator, ScheduleValidator>();

// 7. Optimization Services (Google OR-Tools GLOP + Pure Simplex Fallback)
builder.Services.AddSingleton<SimplexFallbackOptimizer>();
builder.Services.AddScoped<IEnergyOptimizer, EnergyOptimizer>();

// 8. Application Orchestration Services
builder.Services.AddScoped<IEnergyService, EnergyService>();

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "GridWise API v1");
    c.RoutePrefix = "swagger";
});

// Enable default files (index.html) and static files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// Also map /dashboard to root for convenience
app.MapGet("/dashboard", () => Results.Redirect("/"));

app.UseRouting();

// Global Exception Handler Middleware to ensure controlled 500 without leaking secrets
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception occurred during request execution.");

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var errorResponse = new ApiErrorResponse
            {
                Error = "Internal Server Error",
                Message = "A controlled server error occurred while processing the request.",
                StatusCode = StatusCodes.Status500InternalServerError
            };

            await context.Response.WriteAsJsonAsync(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
        }
    }
});

app.MapControllers();

app.Run();

// Make Program accessible for WebApplicationFactory in integration tests
public partial class Program { }
