using System.Text.Json.Serialization;

namespace CampusGrid.Models.Domain;

public class Battery
{
    [JsonPropertyName("capacity_kwh")]
    [JsonRequired]
    public double CapacityKwh { get; set; }

    [JsonPropertyName("initial_energy_kwh")]
    [JsonRequired]
    public double InitialEnergyKwh { get; set; }

    [JsonPropertyName("minimum_energy_kwh")]
    [JsonRequired]
    public double MinimumEnergyKwh { get; set; }

    [JsonPropertyName("max_charge_kwh_per_hour")]
    [JsonRequired]
    public double MaxChargeKwhPerHour { get; set; }

    [JsonPropertyName("max_discharge_kwh_per_hour")]
    [JsonRequired]
    public double MaxDischargeKwhPerHour { get; set; }
}
