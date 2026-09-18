using System.Text.Json.Serialization;

namespace CampusGrid.Models.Domain;

public class HourData
{
    [JsonPropertyName("hour")]
    [JsonRequired]
    public int Hour { get; set; }

    [JsonPropertyName("demand_kwh")]
    [JsonRequired]
    public double DemandKwh { get; set; }

    [JsonPropertyName("solar_kwh")]
    [JsonRequired]
    public double SolarKwh { get; set; }

    [JsonPropertyName("tariff_bdt_per_kwh")]
    [JsonRequired]
    public double TariffBdtPerKwh { get; set; }
}
