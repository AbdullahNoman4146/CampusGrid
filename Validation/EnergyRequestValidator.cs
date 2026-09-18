using CampusGrid.Models.Dto;

namespace CampusGrid.Validation;

public interface IEnergyRequestValidator
{
    void Validate(EnergyRequest request);
}

public class EnergyRequestValidator : IEnergyRequestValidator
{
    public void Validate(EnergyRequest request)
    {
        var errors = new List<string>();

        if (request == null)
        {
            throw new InvalidSemanticInputException("Request payload cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(request.ScenarioId))
        {
            errors.Add("Field 'scenario_id' is required and cannot be empty.");
        }

        if (request.OperatorNotes == null || request.OperatorNotes.Count == 0)
        {
            errors.Add("Field 'operator_notes' must contain between 1 and 3 operator notes.");
        }
        else
        {
            if (request.OperatorNotes.Count > 3)
            {
                errors.Add("Field 'operator_notes' cannot contain more than 3 operator notes.");
            }

            for (int i = 0; i < request.OperatorNotes.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(request.OperatorNotes[i]))
                {
                    errors.Add($"Operator note at index {i} cannot be empty.");
                }
            }
        }

        if (request.Hours == null || request.Hours.Count != 24)
        {
            errors.Add($"Field 'hours' must contain exactly 24 hour records. Found {request.Hours?.Count ?? 0}.");
        }
        else
        {
            var seenHours = new HashSet<int>();
            for (int i = 0; i < request.Hours.Count; i++)
            {
                var h = request.Hours[i];
                if (h.Hour < 0 || h.Hour > 23)
                {
                    errors.Add($"Hour record at index {i} has invalid hour value {h.Hour}. Must be 0 to 23.");
                }

                if (!seenHours.Add(h.Hour))
                {
                    errors.Add($"Duplicate hour {h.Hour} found in 'hours' array.");
                }

                if (h.DemandKwh < 0)
                {
                    errors.Add($"Hour {h.Hour}: demand_kwh must be non-negative (received {h.DemandKwh}).");
                }

                if (h.SolarKwh < 0)
                {
                    errors.Add($"Hour {h.Hour}: solar_kwh must be non-negative (received {h.SolarKwh}).");
                }

                if (h.TariffBdtPerKwh < 0)
                {
                    errors.Add($"Hour {h.Hour}: tariff_bdt_per_kwh must be non-negative (received {h.TariffBdtPerKwh}).");
                }
            }

            if (seenHours.Count == 24)
            {
                for (int expected = 0; expected < 24; expected++)
                {
                    if (!seenHours.Contains(expected))
                    {
                        errors.Add($"Missing hour {expected} in 'hours' array.");
                    }
                }
            }
        }

        if (request.Battery == null)
        {
            errors.Add("Field 'battery' is required.");
        }
        else
        {
            var b = request.Battery;
            if (b.CapacityKwh <= 0)
            {
                errors.Add($"battery.capacity_kwh must be greater than zero (received {b.CapacityKwh}).");
            }

            if (b.MinimumEnergyKwh < 0)
            {
                errors.Add($"battery.minimum_energy_kwh must be non-negative (received {b.MinimumEnergyKwh}).");
            }

            if (b.InitialEnergyKwh < 0)
            {
                errors.Add($"battery.initial_energy_kwh must be non-negative (received {b.InitialEnergyKwh}).");
            }

            if (b.CapacityKwh > 0)
            {
                if (b.MinimumEnergyKwh > b.CapacityKwh)
                {
                    errors.Add($"battery.minimum_energy_kwh ({b.MinimumEnergyKwh}) cannot exceed capacity_kwh ({b.CapacityKwh}).");
                }

                if (b.InitialEnergyKwh > b.CapacityKwh)
                {
                    errors.Add($"battery.initial_energy_kwh ({b.InitialEnergyKwh}) cannot exceed capacity_kwh ({b.CapacityKwh}).");
                }

                if (b.InitialEnergyKwh < b.MinimumEnergyKwh)
                {
                    errors.Add($"battery.initial_energy_kwh ({b.InitialEnergyKwh}) cannot be less than minimum_energy_kwh ({b.MinimumEnergyKwh}).");
                }
            }

            if (b.MaxChargeKwhPerHour < 0)
            {
                errors.Add($"battery.max_charge_kwh_per_hour must be non-negative (received {b.MaxChargeKwhPerHour}).");
            }

            if (b.MaxDischargeKwhPerHour < 0)
            {
                errors.Add($"battery.max_discharge_kwh_per_hour must be non-negative (received {b.MaxDischargeKwhPerHour}).");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidSemanticInputException("Request validation failed.", errors);
        }
    }
}
