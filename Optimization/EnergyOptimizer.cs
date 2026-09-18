using Google.OrTools.LinearSolver;
using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;
using CampusGrid.Validation;

namespace CampusGrid.Optimization;

public class EnergyOptimizer : IEnergyOptimizer
{
    private readonly SimplexFallbackOptimizer _fallbackOptimizer;
    private readonly ILogger<EnergyOptimizer> _logger;

    public EnergyOptimizer(
        SimplexFallbackOptimizer fallbackOptimizer,
        ILogger<EnergyOptimizer> logger)
    {
        _fallbackOptimizer = fallbackOptimizer;
        _logger = logger;
    }

    public OptimizationResult Optimize(EnergyRequest request, IEnumerable<DirectiveInterpretationDto> directives)
    {
        request = EnergyRequestNormalizer.NormalizeHours(request);

        // 1. Calculate effective parameters per hour based on active directives
        var effectiveSolar = new double[24];
        var minReserve = new double[24];
        var maxGrid = new double[24];
        var maxCharge = new double[24];
        var maxDischarge = new double[24];

        for (int h = 0; h < 24; h++)
        {
            effectiveSolar[h] = request.Hours[h].SolarKwh;
            minReserve[h] = request.Battery.MinimumEnergyKwh;
            maxGrid[h] = double.PositiveInfinity;
            maxCharge[h] = request.Battery.MaxChargeKwhPerHour;
            maxDischarge[h] = request.Battery.MaxDischargeKwhPerHour;
        }

        foreach (var dir in directives.Where(d => d.Applies && d.StructuredAdjustment != null))
        {
            var adj = dir.StructuredAdjustment!;
            var hours = adj.Hours ?? new List<int>();

            switch (dir.DirectiveType)
            {
                case DirectiveType.SolarReduction:
                    var factor = adj.Factor ?? 1.0;
                    foreach (var h in hours.Where(h => h >= 0 && h < 24))
                    {
                        effectiveSolar[h] = Math.Min(effectiveSolar[h], request.Hours[h].SolarKwh * factor);
                    }
                    break;

                case DirectiveType.MinimumBatteryReserve:
                    var res = adj.MinimumEnergyKwh ?? request.Battery.MinimumEnergyKwh;
                    foreach (var h in hours.Where(h => h >= 0 && h < 24))
                    {
                        minReserve[h] = Math.Max(minReserve[h], res);
                    }
                    break;

                case DirectiveType.MaxGridWindow:
                    var cap = adj.MaxGridKwh ?? double.PositiveInfinity;
                    foreach (var h in hours.Where(h => h >= 0 && h < 24))
                    {
                        maxGrid[h] = Math.Min(maxGrid[h], cap);
                    }
                    break;

                case DirectiveType.NoChargeWindow:
                    foreach (var h in hours.Where(h => h >= 0 && h < 24))
                    {
                        maxCharge[h] = 0.0;
                    }
                    break;

                case DirectiveType.NoDischargeWindow:
                    foreach (var h in hours.Where(h => h >= 0 && h < 24))
                    {
                        maxDischarge[h] = 0.0;
                    }
                    break;
            }
        }

        // 2. Try primary optimization via Google OR-Tools GLOP solver
        try
        {
            return SolveWithOrTools(request, effectiveSolar, minReserve, maxGrid, maxCharge, maxDischarge);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OR-Tools solver failed or is unavailable. Executing pure C# Simplex fallback optimizer.");
            return _fallbackOptimizer.Optimize(request, effectiveSolar, minReserve, maxGrid, maxCharge, maxDischarge);
        }
    }

    private OptimizationResult SolveWithOrTools(
        EnergyRequest request,
        double[] effectiveSolar,
        double[] minReserve,
        double[] maxGrid,
        double[] maxCharge,
        double[] maxDischarge)
    {
        Solver solver = Solver.CreateSolver("GLOP");
        if (solver == null)
        {
            throw new InvalidOperationException("Could not create Google OR-Tools GLOP solver instance.");
        }

        int T = 24;
        var gridVars = new Variable[T];
        var solarVars = new Variable[T];
        var chargeVars = new Variable[T];
        var dischargeVars = new Variable[T];
        var batteryVars = new Variable[T];

        for (int t = 0; t < T; t++)
        {
            double gridUpper = double.IsInfinity(maxGrid[t]) ? double.PositiveInfinity : maxGrid[t];
            gridVars[t] = solver.MakeNumVar(0.0, gridUpper, $"grid_{t}");
            solarVars[t] = solver.MakeNumVar(0.0, effectiveSolar[t], $"solar_{t}");
            chargeVars[t] = solver.MakeNumVar(0.0, maxCharge[t], $"charge_{t}");
            dischargeVars[t] = solver.MakeNumVar(0.0, maxDischarge[t], $"discharge_{t}");
            batteryVars[t] = solver.MakeNumVar(minReserve[t], request.Battery.CapacityKwh, $"battery_{t}");
        }

        // 1. Energy Balance constraint for each hour:
        // grid[t] + solar_used[t] + discharge[t] - charge[t] == demand[t]
        for (int t = 0; t < T; t++)
        {
            double demand = request.Hours[t].DemandKwh;
            Constraint balance = solver.MakeConstraint(demand, demand, $"balance_{t}");
            balance.SetCoefficient(gridVars[t], 1.0);
            balance.SetCoefficient(solarVars[t], 1.0);
            balance.SetCoefficient(dischargeVars[t], 1.0);
            balance.SetCoefficient(chargeVars[t], -1.0);
        }

        // 2. Battery Dynamics state transition constraints:
        // For t = 0: battery[0] - charge[0] + discharge[0] == initial_energy
        Constraint trans0 = solver.MakeConstraint(request.Battery.InitialEnergyKwh, request.Battery.InitialEnergyKwh, "trans_0");
        trans0.SetCoefficient(batteryVars[0], 1.0);
        trans0.SetCoefficient(chargeVars[0], -1.0);
        trans0.SetCoefficient(dischargeVars[0], 1.0);

        // For t = 1..23: battery[t] - battery[t-1] - charge[t] + discharge[t] == 0
        for (int t = 1; t < T; t++)
        {
            Constraint trans = solver.MakeConstraint(0.0, 0.0, $"trans_{t}");
            trans.SetCoefficient(batteryVars[t], 1.0);
            trans.SetCoefficient(batteryVars[t - 1], -1.0);
            trans.SetCoefficient(chargeVars[t], -1.0);
            trans.SetCoefficient(dischargeVars[t], 1.0);
        }

        // 3. End-of-Day Neutrality constraint:
        // battery[23] == initial_energy
        Constraint neutrality = solver.MakeConstraint(request.Battery.InitialEnergyKwh, request.Battery.InitialEnergyKwh, "neutrality");
        neutrality.SetCoefficient(batteryVars[23], 1.0);

        // 4. Objective:
        // Minimize SUM(tariff[t] * grid[t] + 1e-5 * (charge[t] + discharge[t]))
        Objective objective = solver.Objective();
        for (int t = 0; t < T; t++)
        {
            objective.SetCoefficient(gridVars[t], request.Hours[t].TariffBdtPerKwh);
            objective.SetCoefficient(chargeVars[t], 1e-5);
            objective.SetCoefficient(dischargeVars[t], 1e-5);
        }
        objective.SetMinimization();

        var resultStatus = solver.Solve();
        if (resultStatus != Solver.ResultStatus.OPTIMAL && resultStatus != Solver.ResultStatus.FEASIBLE)
        {
            return new OptimizationResult
            {
                Success = false,
                ErrorMessage = $"The scenario is mathematically infeasible under given constraints (Solver status: {resultStatus})."
            };
        }

        var hourlyPlan = new List<HourlyPlanDto>();
        double totalGrid = 0;
        double totalCost = 0;
        double peakGrid = 0;

        for (int t = 0; t < T; t++)
        {
            double grid = Math.Max(0.0, Math.Round(gridVars[t].SolutionValue(), 4));
            double solar = Math.Max(0.0, Math.Round(solarVars[t].SolutionValue(), 4));
            double charge = Math.Max(0.0, Math.Round(chargeVars[t].SolutionValue(), 4));
            double discharge = Math.Max(0.0, Math.Round(dischargeVars[t].SolutionValue(), 4));
            double batteryEnergy = Math.Max(0.0, Math.Round(batteryVars[t].SolutionValue(), 4));

            string action = BatteryAction.Idle;
            double batteryKwh = 0.0;

            if (charge > 1e-3)
            {
                action = BatteryAction.Charge;
                batteryKwh = charge;
            }
            else if (discharge > 1e-3)
            {
                action = BatteryAction.Discharge;
                batteryKwh = discharge;
            }

            hourlyPlan.Add(new HourlyPlanDto
            {
                Hour = t,
                GridKwh = grid,
                SolarUsedKwh = solar,
                BatteryAction = action,
                BatteryKwh = batteryKwh,
                BatteryEnergyAfterKwh = batteryEnergy
            });

            totalGrid += grid;
            totalCost += grid * request.Hours[t].TariffBdtPerKwh;
            peakGrid = Math.Max(peakGrid, grid);
        }

        return new OptimizationResult
        {
            Success = true,
            HourlyPlan = hourlyPlan,
            TotalGridKwh = Math.Round(totalGrid, 2),
            TotalCostBdt = Math.Round(totalCost, 2),
            PeakGridKwh = Math.Round(peakGrid, 2)
        };
    }
}
