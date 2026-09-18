using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using CampusGrid.Models.Common;
using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;
using Xunit;

namespace CampusGrid.Tests;

public class RequestValidationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public RequestValidationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task OptimizeEnergy_MalformedJson_Returns400BadRequest()
    {
        var invalidJson = "{ \"scenario_id\": \"GRID-1\", \"hours\": [ not a valid json ";
        var content = new StringContent(invalidJson, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/optimize-energy", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ApiErrorResponse>(body, JsonOptions);
        Assert.NotNull(error);
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task OptimizeEnergy_MissingHours_Returns422UnprocessableEntity()
    {
        var request = new EnergyRequest
        {
            ScenarioId = "TEST-MISSING-HOURS",
            OperatorNotes = new List<string> { "Test note" },
            Hours = new List<HourData>(), // 0 hours instead of 24
            Battery = new Battery
            {
                CapacityKwh = 200,
                InitialEnergyKwh = 100,
                MinimumEnergyKwh = 30,
                MaxChargeKwhPerHour = 50,
                MaxDischargeKwhPerHour = 50
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/optimize-energy", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ApiErrorResponse>(body, JsonOptions);
        Assert.NotNull(error);
        Assert.Equal(422, error.StatusCode);
        Assert.Contains(error.Details ?? new List<string>(), d => d.Contains("24"));
    }

    [Fact]
    public async Task OptimizeEnergy_NegativeDemand_Returns422UnprocessableEntity()
    {
        var request = CreateValidRequest();
        request.Hours[5].DemandKwh = -50; // negative demand

        var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/optimize-energy", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ApiErrorResponse>(body, JsonOptions);
        Assert.NotNull(error);
        Assert.Equal(422, error.StatusCode);
    }

    [Fact]
    public async Task OptimizeEnergy_BatteryInitialBelowMinimum_Returns422UnprocessableEntity()
    {
        var request = CreateValidRequest();
        request.Battery.InitialEnergyKwh = 20;
        request.Battery.MinimumEnergyKwh = 50; // initial < minimum!

        var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/optimize-energy", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ApiErrorResponse>(body, JsonOptions);
        Assert.NotNull(error);
        Assert.Equal(422, error.StatusCode);
    }

    private static EnergyRequest CreateValidRequest()
    {
        var hours = new List<HourData>();
        for (int i = 0; i < 24; i++)
        {
            hours.Add(new HourData
            {
                Hour = i,
                DemandKwh = 100,
                SolarKwh = (i >= 8 && i <= 16) ? 50 : 0,
                TariffBdtPerKwh = 10
            });
        }

        return new EnergyRequest
        {
            ScenarioId = "VALID-BASE",
            OperatorNotes = new List<string> { "Standard operations today." },
            Hours = hours,
            Battery = new Battery
            {
                CapacityKwh = 200,
                InitialEnergyKwh = 100,
                MinimumEnergyKwh = 30,
                MaxChargeKwhPerHour = 50,
                MaxDischargeKwhPerHour = 50
            }
        };
    }
}
