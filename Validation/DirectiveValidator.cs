using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;

namespace CampusGrid.Validation;

public interface IDirectiveValidator
{
    void Validate(DirectiveInterpretationDto directive, Battery? battery = null);
    void ValidateAll(IEnumerable<DirectiveInterpretationDto> directives, Battery? battery = null);
}

public class DirectiveValidator : IDirectiveValidator
{
    public void Validate(DirectiveInterpretationDto directive, Battery? battery = null)
    {
        var errors = new List<string>();

        if (directive == null)
        {
            throw new DirectiveValidationException("Directive interpretation object cannot be null.");
        }

        // 1. Allowed directive types only
        if (string.IsNullOrWhiteSpace(directive.DirectiveType) || !DirectiveType.All.Contains(directive.DirectiveType))
        {
            errors.Add($"Invalid directive_type: '{directive.DirectiveType}'. Allowed types: {string.Join(", ", DirectiveType.All)}.");
        }

        if (string.IsNullOrWhiteSpace(directive.Explanation))
        {
            errors.Add("Directive explanation cannot be empty.");
        }

        // 2. No-Op validation
        if (string.Equals(directive.DirectiveType, DirectiveType.NoOp, StringComparison.OrdinalIgnoreCase))
        {
            if (directive.Applies)
            {
                errors.Add("For 'no_op' directive, 'applies' must be false.");
            }

            if (directive.StructuredAdjustment != null)
            {
                errors.Add("For 'no_op' directive, 'structured_adjustment' must be null.");
            }

            if (errors.Count > 0)
            {
                throw new DirectiveValidationException("Guardrail validation failed for no_op directive.", errors);
            }

            return;
        }

        // 3. Non-NoOp validation: applies must be true, structured_adjustment must not be null
        if (!directive.Applies)
        {
            errors.Add($"For directive '{directive.DirectiveType}', 'applies' must be true.");
        }

        if (directive.StructuredAdjustment == null)
        {
            errors.Add($"For directive '{directive.DirectiveType}', 'structured_adjustment' cannot be null.");
            throw new DirectiveValidationException("Guardrail validation failed: missing structured_adjustment.", errors);
        }

        var adj = directive.StructuredAdjustment;

        // 4. Hours validation: integers, between 0-23, unique, ascending order
        if (adj.Hours == null || adj.Hours.Count == 0)
        {
            errors.Add($"Directive '{directive.DirectiveType}' must specify at least one hour in 'hours'.");
        }
        else
        {
            for (int i = 0; i < adj.Hours.Count; i++)
            {
                int h = adj.Hours[i];
                if (h < 0 || h > 23)
                {
                    errors.Add($"Hour value {h} is out of range. Hours must be integers between 0 and 23.");
                }

                if (i > 0)
                {
                    if (h <= adj.Hours[i - 1])
                    {
                        errors.Add($"Hours must be unique and strictly sorted in ascending order. Found {adj.Hours[i - 1]} followed by {h}.");
                    }
                }
            }
        }

        // 5. Directive-specific parameter validation and checking for invented parameters
        switch (directive.DirectiveType)
        {
            case DirectiveType.SolarReduction:
                if (!adj.Factor.HasValue)
                {
                    errors.Add("Directive 'solar_reduction' requires 'factor'.");
                }
                else if (!double.IsFinite(adj.Factor.Value))
                {
                    errors.Add("Solar factor must be a finite number.");
                }
                else if (adj.Factor.Value < 0.0 || adj.Factor.Value > 1.0)
                {
                    errors.Add($"Solar factor must be between 0.0 and 1.0. Received {adj.Factor.Value}.");
                }

                // Check for invented parameters
                if (adj.MinimumEnergyKwh.HasValue)
                {
                    errors.Add("Invented parameter: 'minimum_energy_kwh' is not allowed on 'solar_reduction'.");
                }
                if (adj.MaxGridKwh.HasValue)
                {
                    errors.Add("Invented parameter: 'max_grid_kwh' is not allowed on 'solar_reduction'.");
                }
                break;

            case DirectiveType.MinimumBatteryReserve:
                if (!adj.MinimumEnergyKwh.HasValue)
                {
                    errors.Add("Directive 'minimum_battery_reserve' requires 'minimum_energy_kwh'.");
                }
                else
                {
                    if (!double.IsFinite(adj.MinimumEnergyKwh.Value))
                    {
                        errors.Add("minimum_energy_kwh must be a finite number.");
                    }
                    else if (adj.MinimumEnergyKwh.Value < 0)
                    {
                        errors.Add($"minimum_energy_kwh must be non-negative. Received {adj.MinimumEnergyKwh.Value}.");
                    }

                    if (battery != null && battery.CapacityKwh > 0 && adj.MinimumEnergyKwh.Value > battery.CapacityKwh)
                    {
                        errors.Add($"minimum_energy_kwh ({adj.MinimumEnergyKwh.Value}) cannot exceed battery capacity ({battery.CapacityKwh}).");
                    }
                }

                // Check for invented parameters
                if (adj.Factor.HasValue)
                {
                    errors.Add("Invented parameter: 'factor' is not allowed on 'minimum_battery_reserve'.");
                }
                if (adj.MaxGridKwh.HasValue)
                {
                    errors.Add("Invented parameter: 'max_grid_kwh' is not allowed on 'minimum_battery_reserve'.");
                }
                break;

            case DirectiveType.MaxGridWindow:
                if (!adj.MaxGridKwh.HasValue)
                {
                    errors.Add("Directive 'max_grid_window' requires 'max_grid_kwh'.");
                }
                else if (!double.IsFinite(adj.MaxGridKwh.Value))
                {
                    errors.Add("max_grid_kwh must be a finite number.");
                }
                else if (adj.MaxGridKwh.Value < 0)
                {
                    errors.Add($"max_grid_kwh must be non-negative. Received {adj.MaxGridKwh.Value}.");
                }

                // Check for invented parameters
                if (adj.Factor.HasValue)
                {
                    errors.Add("Invented parameter: 'factor' is not allowed on 'max_grid_window'.");
                }
                if (adj.MinimumEnergyKwh.HasValue)
                {
                    errors.Add("Invented parameter: 'minimum_energy_kwh' is not allowed on 'max_grid_window'.");
                }
                break;

            case DirectiveType.NoChargeWindow:
                // Only hours allowed
                if (adj.Factor.HasValue)
                {
                    errors.Add("Invented parameter: 'factor' is not allowed on 'no_charge_window'.");
                }
                if (adj.MinimumEnergyKwh.HasValue)
                {
                    errors.Add("Invented parameter: 'minimum_energy_kwh' is not allowed on 'no_charge_window'.");
                }
                if (adj.MaxGridKwh.HasValue)
                {
                    errors.Add("Invented parameter: 'max_grid_kwh' is not allowed on 'no_charge_window'.");
                }
                break;

            case DirectiveType.NoDischargeWindow:
                // Only hours allowed
                if (adj.Factor.HasValue)
                {
                    errors.Add("Invented parameter: 'factor' is not allowed on 'no_discharge_window'.");
                }
                if (adj.MinimumEnergyKwh.HasValue)
                {
                    errors.Add("Invented parameter: 'minimum_energy_kwh' is not allowed on 'no_discharge_window'.");
                }
                if (adj.MaxGridKwh.HasValue)
                {
                    errors.Add("Invented parameter: 'max_grid_kwh' is not allowed on 'no_discharge_window'.");
                }
                break;
        }

        if (errors.Count > 0)
        {
            throw new DirectiveValidationException(
                $"Guardrail validation failed for directive #{directive.NoteIndex} ('{directive.DirectiveType}').",
                errors);
        }
    }

    public void ValidateAll(IEnumerable<DirectiveInterpretationDto> directives, Battery? battery = null)
    {
        if (directives == null) return;
        foreach (var directive in directives)
        {
            Validate(directive, battery);
        }
    }
}
