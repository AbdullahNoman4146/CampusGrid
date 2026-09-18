using System.Text.Json.Serialization;

namespace CampusGrid.Models.Domain;

public class StructuredAdjustment
{
    [JsonPropertyName("hours")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? Hours { get; set; }

    [JsonPropertyName("factor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Factor { get; set; }

    [JsonPropertyName("minimum_energy_kwh")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinimumEnergyKwh { get; set; }

    [JsonPropertyName("max_grid_kwh")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaxGridKwh { get; set; }
}
