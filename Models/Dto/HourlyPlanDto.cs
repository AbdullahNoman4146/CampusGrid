using System.Text.Json.Serialization;

namespace CampusGrid.Models.Dto;

public class HourlyPlanDto
{
    [JsonPropertyName("hour")]
    public int Hour { get; set; }

    [JsonPropertyName("grid_kwh")]
    public double GridKwh { get; set; }

    [JsonPropertyName("solar_used_kwh")]
    public double SolarUsedKwh { get; set; }

    [JsonPropertyName("battery_action")]
    public string BatteryAction { get; set; } = Domain.BatteryAction.Idle;

    [JsonPropertyName("battery_kwh")]
    public double BatteryKwh { get; set; }

    [JsonPropertyName("battery_energy_after_kwh")]
    public double BatteryEnergyAfterKwh { get; set; }
}
