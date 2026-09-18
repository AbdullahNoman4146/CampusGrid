using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;
using Xunit;

namespace CampusGrid.Tests;

public class SampleCasesTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public SampleCasesTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    public static IEnumerable<object[]> GetSampleCases()
    {
        var testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample_cases.json");
        if (!File.Exists(testDataPath))
        {
            testDataPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "sample_cases.json");
        }

        var json = File.ReadAllText(testDataPath);
        using var doc = JsonDocument.Parse(json);
        var casesArray = doc.RootElement.GetProperty("cases");

        foreach (var c in casesArray.EnumerateArray())
        {
            var caseId = c.GetProperty("id").GetString()!;
            var inputJson = c.GetProperty("input").GetRawText();
            var expectedJson = c.GetProperty("expected_output").GetRawText();
            yield return new object[] { caseId, inputJson, expectedJson };
        }
    }

    [Theory]
    [MemberData(nameof(GetSampleCases))]
    public async Task OptimizeEnergy_SampleCase_SatisfiesAllConstraintsAndMatchesReference(
        string caseId,
        string inputJson,
        string expectedJson)
    {
        // Arrange
        var requestContent = new StringContent(inputJson, Encoding.UTF8, "application/json");

        // Act
        var httpResponse = await _client.PostAsync("/optimize-energy", requestContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

        var responseBody = await httpResponse.Content.ReadAsStringAsync();
        var actualResponse = JsonSerializer.Deserialize<EnergyResponse>(responseBody, JsonOptions);
        var expectedResponse = JsonSerializer.Deserialize<EnergyResponse>(expectedJson, JsonOptions);
        var originalRequest = JsonSerializer.Deserialize<EnergyRequest>(inputJson, JsonOptions);

        Assert.NotNull(actualResponse);
        Assert.NotNull(expectedResponse);
        Assert.NotNull(originalRequest);

        // 1. Verify Scenario ID
        Assert.Equal(caseId, actualResponse.ScenarioId);

        // 2. Verify Directive Interpretation
        Assert.Equal(expectedResponse.DirectiveInterpretation.Count, actualResponse.DirectiveInterpretation.Count);

        for (int i = 0; i < expectedResponse.DirectiveInterpretation.Count; i++)
        {
            var expDir = expectedResponse.DirectiveInterpretation[i];
            var actDir = actualResponse.DirectiveInterpretation[i];

            Assert.Equal(expDir.NoteIndex, actDir.NoteIndex);
            Assert.Equal(expDir.Applies, actDir.Applies);
            Assert.Equal(expDir.DirectiveType, actDir.DirectiveType);

            if (!expDir.Applies)
            {
                Assert.Null(actDir.StructuredAdjustment);
            }
            else
            {
                Assert.NotNull(actDir.StructuredAdjustment);
                Assert.NotNull(expDir.StructuredAdjustment);

                if (expDir.StructuredAdjustment.Hours != null)
                {
                    Assert.Equal(expDir.StructuredAdjustment.Hours, actDir.StructuredAdjustment.Hours);
                }

                if (expDir.StructuredAdjustment.Factor.HasValue)
                {
                    Assert.NotNull(actDir.StructuredAdjustment.Factor);
                    Assert.True(Math.Abs(expDir.StructuredAdjustment.Factor.Value - actDir.StructuredAdjustment.Factor.Value) < 0.01,
                        $"Factor mismatch for case {caseId}: expected {expDir.StructuredAdjustment.Factor.Value}, got {actDir.StructuredAdjustment.Factor.Value}");
                }

                if (expDir.StructuredAdjustment.MinimumEnergyKwh.HasValue)
                {
                    Assert.NotNull(actDir.StructuredAdjustment.MinimumEnergyKwh);
                    Assert.True(Math.Abs(expDir.StructuredAdjustment.MinimumEnergyKwh.Value - actDir.StructuredAdjustment.MinimumEnergyKwh.Value) < 0.01,
                        $"Reserve mismatch for case {caseId}: expected {expDir.StructuredAdjustment.MinimumEnergyKwh.Value}, got {actDir.StructuredAdjustment.MinimumEnergyKwh.Value}");
                }

                if (expDir.StructuredAdjustment.MaxGridKwh.HasValue)
                {
                    Assert.NotNull(actDir.StructuredAdjustment.MaxGridKwh);
                    Assert.True(Math.Abs(expDir.StructuredAdjustment.MaxGridKwh.Value - actDir.StructuredAdjustment.MaxGridKwh.Value) < 0.01,
                        $"MaxGrid mismatch for case {caseId}: expected {expDir.StructuredAdjustment.MaxGridKwh.Value}, got {actDir.StructuredAdjustment.MaxGridKwh.Value}");
                }
            }
        }

        // 3. Verify Hourly Plan length
        Assert.Equal(24, actualResponse.HourlyPlan.Count);

        // 4. Verify End-of-Day Battery Neutrality
        var finalBatteryEnergy = actualResponse.HourlyPlan[23].BatteryEnergyAfterKwh;
        Assert.True(Math.Abs(finalBatteryEnergy - originalRequest.Battery.InitialEnergyKwh) <= 0.01,
            $"Battery neutrality violated in {caseId}: expected {originalRequest.Battery.InitialEnergyKwh}, got {finalBatteryEnergy}");

        // 5. Verify Hourly Energy Balance for each hour
        double prevEnergy = originalRequest.Battery.InitialEnergyKwh;
        for (int h = 0; h < 24; h++)
        {
            var plan = actualResponse.HourlyPlan[h];
            var hourData = originalRequest.Hours[h];

            double charge = plan.BatteryAction == BatteryAction.Charge ? plan.BatteryKwh : 0.0;
            double discharge = plan.BatteryAction == BatteryAction.Discharge ? plan.BatteryKwh : 0.0;

            double lhs = plan.GridKwh + plan.SolarUsedKwh + discharge;
            double rhs = hourData.DemandKwh + charge;
            Assert.True(Math.Abs(lhs - rhs) <= 0.01,
                $"Energy balance violated at hour {h} in {caseId}: {lhs} != {rhs}");

            // Transition check
            double expectedEnergy = prevEnergy + charge - discharge;
            Assert.True(Math.Abs(plan.BatteryEnergyAfterKwh - expectedEnergy) <= 0.01,
                $"Battery transition violated at hour {h} in {caseId}: {plan.BatteryEnergyAfterKwh} != {expectedEnergy}");

            prevEnergy = plan.BatteryEnergyAfterKwh;
        }

        // 6. Verify optimal total cost within the canonical 0.01 BDT tolerance
        double costDiff = Math.Abs(actualResponse.TotalCostBdt - expectedResponse.TotalCostBdt);
        Assert.True(costDiff <= 0.01,
            $"Total cost mismatch for {caseId}: expected {expectedResponse.TotalCostBdt}, got {actualResponse.TotalCostBdt} (Diff: {costDiff})");
    }
}
