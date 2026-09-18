using System.Text.Json;
using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;
using CampusGrid.Optimization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CampusGrid.Tests;

public class OptimizationEngineTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly EnergyOptimizer _optimizer;

    public OptimizationEngineTests()
    {
        var fallback = new SimplexFallbackOptimizer();
        _optimizer = new EnergyOptimizer(fallback, NullLogger<EnergyOptimizer>.Instance);
    }

    [Fact]
    public void Optimize_Sample01_DirectOptimization_MatchesOptimalCost()
    {
        var testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample_cases.json");
        if (!File.Exists(testDataPath))
        {
            testDataPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "sample_cases.json");
        }

        var json = File.ReadAllText(testDataPath);
        using var doc = JsonDocument.Parse(json);
        var sample01 = doc.RootElement.GetProperty("cases")[0];
        var request = JsonSerializer.Deserialize<EnergyRequest>(sample01.GetProperty("input").GetRawText(), JsonOptions)!;

        var directives = new List<DirectiveInterpretationDto>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = DirectiveType.SolarReduction,
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = new List<int> { 12, 13 },
                    Factor = 0.25
                }
            },
            new()
            {
                NoteIndex = 1,
                Applies = false,
                DirectiveType = DirectiveType.NoOp,
                StructuredAdjustment = null
            }
        };

        var result = _optimizer.Optimize(request, directives);

        Assert.True(result.Success);
        Assert.Equal(24, result.HourlyPlan.Count);

        // Optimal total cost is 38365 BDT
        Assert.True(Math.Abs(result.TotalCostBdt - 38365) <= 0.01,
            $"Optimal cost mismatch: {result.TotalCostBdt}, expected 38365");

        // Optimal total grid is 2692.5 kWh
        Assert.True(Math.Abs(result.TotalGridKwh - 2692.5) <= 0.01,
            $"Total grid mismatch: {result.TotalGridKwh}, expected 2692.5");

        // Peak grid is 175 kWh
        Assert.True(Math.Abs(result.PeakGridKwh - 175) <= 0.01,
            $"Peak grid mismatch: {result.PeakGridKwh}, expected 175");

        // Battery neutrality: final energy equals initial energy (110 kWh)
        Assert.True(Math.Abs(result.HourlyPlan[23].BatteryEnergyAfterKwh - request.Battery.InitialEnergyKwh) <= 0.01);
    }

    [Fact]
    public void Optimize_NoChargeWindow_PreventsChargingInWindow()
    {
        var testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample_cases.json");
        var json = File.ReadAllText(testDataPath);
        using var doc = JsonDocument.Parse(json);
        var sample02 = doc.RootElement.GetProperty("cases")[1]; // SAMPLE-02
        var request = JsonSerializer.Deserialize<EnergyRequest>(sample02.GetProperty("input").GetRawText(), JsonOptions)!;

        var directives = new List<DirectiveInterpretationDto>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = DirectiveType.NoChargeWindow,
                StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 2, 3, 4 } }
            }
        };

        var result = _optimizer.Optimize(request, directives);

        Assert.True(result.Success);
        foreach (int h in new[] { 2, 3, 4 })
        {
            Assert.NotEqual(BatteryAction.Charge, result.HourlyPlan[h].BatteryAction);
        }
    }

    [Fact]
    public void Optimize_NoDischargeWindow_PreventsDischargingInWindow()
    {
        var testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample_cases.json");
        var json = File.ReadAllText(testDataPath);
        using var doc = JsonDocument.Parse(json);
        var sample04 = doc.RootElement.GetProperty("cases")[3]; // SAMPLE-04
        var request = JsonSerializer.Deserialize<EnergyRequest>(sample04.GetProperty("input").GetRawText(), JsonOptions)!;

        var directives = new List<DirectiveInterpretationDto>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = DirectiveType.NoDischargeWindow,
                StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 18, 19 } }
            }
        };

        var result = _optimizer.Optimize(request, directives);

        Assert.True(result.Success);
        foreach (int h in new[] { 18, 19 })
        {
            Assert.NotEqual(BatteryAction.Discharge, result.HourlyPlan[h].BatteryAction);
        }
    }

    [Fact]
    public void Optimize_MaxGridWindow_CapsGridImportInWindow()
    {
        var testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample_cases.json");
        var json = File.ReadAllText(testDataPath);
        using var doc = JsonDocument.Parse(json);
        var sample05 = doc.RootElement.GetProperty("cases")[4]; // SAMPLE-05
        var request = JsonSerializer.Deserialize<EnergyRequest>(sample05.GetProperty("input").GetRawText(), JsonOptions)!;

        var directives = new List<DirectiveInterpretationDto>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = DirectiveType.MaxGridWindow,
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = new List<int> { 18, 19, 20 },
                    MaxGridKwh = 155
                }
            }
        };

        var result = _optimizer.Optimize(request, directives);

        Assert.True(result.Success);
        foreach (int h in new[] { 18, 19, 20 })
        {
            Assert.True(result.HourlyPlan[h].GridKwh <= 155.01,
                $"Hour {h}: grid import {result.HourlyPlan[h].GridKwh} exceeds 155 kWh");
        }
    }

    [Fact]
    public void Optimize_ReorderedHourInput_UsesHourFieldRatherThanArrayPosition()
    {
        var testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample_cases.json");
        var json = File.ReadAllText(testDataPath);
        using var doc = JsonDocument.Parse(json);
        var sample01 = doc.RootElement.GetProperty("cases")[0];
        var request = JsonSerializer.Deserialize<EnergyRequest>(sample01.GetProperty("input").GetRawText(), JsonOptions)!;
        request.Hours.Reverse();

        var directives = new List<DirectiveInterpretationDto>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = DirectiveType.SolarReduction,
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = new List<int> { 12, 13 },
                    Factor = 0.25
                }
            },
            new()
            {
                NoteIndex = 1,
                Applies = false,
                DirectiveType = DirectiveType.NoOp,
                StructuredAdjustment = null
            }
        };

        var result = _optimizer.Optimize(request, directives);

        Assert.True(result.Success);
        Assert.Equal(Enumerable.Range(0, 24), result.HourlyPlan.Select(plan => plan.Hour));
        Assert.True(Math.Abs(result.TotalCostBdt - 38365) <= 0.01,
            $"Reordered input changed optimal cost to {result.TotalCostBdt}.");

        var hourZero = request.Hours.Single(hour => hour.Hour == 0);
        var planZero = result.HourlyPlan[0];
        var discharge = planZero.BatteryAction == BatteryAction.Discharge ? planZero.BatteryKwh : 0;
        var charge = planZero.BatteryAction == BatteryAction.Charge ? planZero.BatteryKwh : 0;
        Assert.True(Math.Abs(planZero.GridKwh + planZero.SolarUsedKwh + discharge -
                             (hourZero.DemandKwh + charge)) <= 0.01);
    }
}
