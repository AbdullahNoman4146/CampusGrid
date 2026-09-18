using System.Text.Json.Serialization;

namespace CampusGrid.Models.Domain;

public class HourData
{
    [JsonPropertyName("hour")]
    public int Hour { get; set; }

    [JsonPropertyName("demand_kwh")]
    public double DemandKwh { get; set; }

    [JsonPropertyName("solar_kwh")]
    public double SolarKwh { get; set; }

    [JsonPropertyName("tariff_bdt_per_kwh")]
    public double TariffBdtPerKwh { get; set; }
}
