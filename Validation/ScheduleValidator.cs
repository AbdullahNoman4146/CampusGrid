using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;

namespace CampusGrid.Validation;

public interface IScheduleValidator
{
    void Validate(EnergyResponse response, EnergyRequest request, IEnumerable<DirectiveInterpretationDto> directives);
}

public class ScheduleValidator : IScheduleValidator
{
    private const double Tolerance = 0.05;

    public void Validate(EnergyResponse response, EnergyRequest request, IEnumerable<DirectiveInterpretationDto> directives)
    {
        var errors = new List<string>();

        if (response.HourlyPlan == null || response.HourlyPlan.Count != 24)
        {
            errors.Add($"Hourly plan must contain exactly 24 hours. Found {response.HourlyPlan?.Count ?? 0}.");
            throw new InvalidSemanticInputException("Schedule validation failed.", errors);
        }

        // Precompute effective solar and directive constraints per hour
        var effectiveSolar = new double[24];
        var minReserve = new double[24];
        var maxGrid = new double[24];
        var noCharge = new bool[24];
        var noDischarge = new bool[24];

        for (int h = 0; h < 24; h++)
        {
            effectiveSolar[h] = request.Hours[h].SolarKwh;
            minReserve[h] = request.Battery.MinimumEnergyKwh;
            maxGrid[h] = double.PositiveInfinity;
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
                        noCharge[h] = true;
                    }
                    break;

                case DirectiveType.NoDischargeWindow:
                    foreach (var h in hours.Where(h => h >= 0 && h < 24))
                    {
                        noDischarge[h] = true;
                    }
                    break;
            }
        }

        double prevEnergy = request.Battery.InitialEnergyKwh;
        double calculatedTotalGrid = 0;
        double calculatedTotalCost = 0;
        double calculatedPeakGrid = 0;

        for (int h = 0; h < 24; h++)
        {
            var plan = response.HourlyPlan[h];
            var hourData = request.Hours[h];

            if (plan.Hour != h)
            {
                errors.Add($"Plan entry #{h} has mismatched hour {plan.Hour}. Expected {h}.");
            }

            if (plan.GridKwh < -Tolerance)
            {
                errors.Add($"Hour {h}: grid_kwh must be non-negative. Received {plan.GridKwh}.");
            }

            if (plan.SolarUsedKwh < -Tolerance)
            {
                errors.Add($"Hour {h}: solar_used_kwh must be non-negative. Received {plan.SolarUsedKwh}.");
            }

            if (plan.SolarUsedKwh > effectiveSolar[h] + Tolerance)
            {
                errors.Add($"Hour {h}: solar_used_kwh ({plan.SolarUsedKwh}) exceeds effective solar ({effectiveSolar[h]}).");
            }

            double charge = 0;
            double discharge = 0;

            if (plan.BatteryAction == BatteryAction.Charge)
            {
                charge = plan.BatteryKwh;
            }
            else if (plan.BatteryAction == BatteryAction.Discharge)
            {
                discharge = plan.BatteryKwh;
            }
            else if (plan.BatteryAction == BatteryAction.Idle)
            {
                if (plan.BatteryKwh > Tolerance)
                {
                    errors.Add($"Hour {h}: battery_kwh must be 0 for action 'idle'. Received {plan.BatteryKwh}.");
                }
            }
            else
            {
                errors.Add($"Hour {h}: invalid battery_action '{plan.BatteryAction}'.");
            }

            // Energy balance check
            double lhs = plan.GridKwh + plan.SolarUsedKwh + discharge;
            double rhs = hourData.DemandKwh + charge;
            if (Math.Abs(lhs - rhs) > Tolerance)
            {
                errors.Add($"Hour {h}: energy balance violated. Grid ({plan.GridKwh}) + Solar ({plan.SolarUsedKwh}) + Discharge ({discharge}) != Demand ({hourData.DemandKwh}) + Charge ({charge}). Diff={Math.Abs(lhs - rhs):F3}.");
            }

            // Rate limits
            if (charge > request.Battery.MaxChargeKwhPerHour + Tolerance)
            {
                errors.Add($"Hour {h}: battery charge rate ({charge}) exceeds max charge limit ({request.Battery.MaxChargeKwhPerHour}).");
            }

            if (discharge > request.Battery.MaxDischargeKwhPerHour + Tolerance)
            {
                errors.Add($"Hour {h}: battery discharge rate ({discharge}) exceeds max discharge limit ({request.Battery.MaxDischargeKwhPerHour}).");
            }

            // Window restrictions
            if (noCharge[h] && charge > Tolerance)
            {
                errors.Add($"Hour {h}: charging is prohibited by no_charge_window directive. Charge={charge}.");
            }

            if (noDischarge[h] && discharge > Tolerance)
            {
                errors.Add($"Hour {h}: discharging is prohibited by no_discharge_window directive. Discharge={discharge}.");
            }

            if (plan.GridKwh > maxGrid[h] + Tolerance)
            {
                errors.Add($"Hour {h}: grid import ({plan.GridKwh}) exceeds max_grid_window limit ({maxGrid[h]}).");
            }

            // Battery energy transition
            double expectedEnergyAfter = prevEnergy + charge - discharge;
            if (Math.Abs(plan.BatteryEnergyAfterKwh - expectedEnergyAfter) > Tolerance)
            {
                errors.Add($"Hour {h}: battery_energy_after_kwh ({plan.BatteryEnergyAfterKwh}) does not match transition from previous ({prevEnergy}) + charge ({charge}) - discharge ({discharge}) = {expectedEnergyAfter}.");
            }

            // Reserve and capacity
            if (plan.BatteryEnergyAfterKwh < minReserve[h] - Tolerance)
            {
                errors.Add($"Hour {h}: battery energy after ({plan.BatteryEnergyAfterKwh}) drops below required minimum reserve ({minReserve[h]}).");
            }

            if (plan.BatteryEnergyAfterKwh > request.Battery.CapacityKwh + Tolerance)
            {
                errors.Add($"Hour {h}: battery energy after ({plan.BatteryEnergyAfterKwh}) exceeds battery capacity ({request.Battery.CapacityKwh}).");
            }

            prevEnergy = plan.BatteryEnergyAfterKwh;
            calculatedTotalGrid += plan.GridKwh;
            calculatedTotalCost += plan.GridKwh * hourData.TariffBdtPerKwh;
            calculatedPeakGrid = Math.Max(calculatedPeakGrid, plan.GridKwh);
        }

        // End-of-day battery neutrality
        double finalEnergy = response.HourlyPlan[23].BatteryEnergyAfterKwh;
        if (Math.Abs(finalEnergy - request.Battery.InitialEnergyKwh) > Tolerance)
        {
            errors.Add($"End-of-day battery neutrality violated. Final battery energy ({finalEnergy}) does not equal initial energy ({request.Battery.InitialEnergyKwh}).");
        }

        // Verify summary fields
        if (Math.Abs(response.TotalGridKwh - calculatedTotalGrid) > Tolerance)
        {
            errors.Add($"total_grid_kwh ({response.TotalGridKwh}) does not match sum of hourly grid ({calculatedTotalGrid}).");
        }

        if (Math.Abs(response.TotalCostBdt - calculatedTotalCost) > Tolerance)
        {
            errors.Add($"total_cost_bdt ({response.TotalCostBdt}) does not match sum of hourly costs ({calculatedTotalCost}).");
        }

        if (Math.Abs(response.PeakGridKwh - calculatedPeakGrid) > Tolerance)
        {
            errors.Add($"peak_grid_kwh ({response.PeakGridKwh}) does not match maximum hourly grid ({calculatedPeakGrid}).");
        }

        if (errors.Count > 0)
        {
            throw new InvalidSemanticInputException("Optimized schedule failed constraint verification.", errors);
        }
    }
}
