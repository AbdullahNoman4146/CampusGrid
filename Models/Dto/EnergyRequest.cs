using System.Text.Json.Serialization;
using CampusGrid.Models.Domain;

namespace CampusGrid.Models.Dto;

public class EnergyRequest
{
    [JsonPropertyName("scenario_id")]
    [JsonRequired]
    public string ScenarioId { get; set; } = string.Empty;

    [JsonPropertyName("operator_notes")]
    [JsonRequired]
    public List<string> OperatorNotes { get; set; } = new();

    [JsonPropertyName("hours")]
    [JsonRequired]
    public List<HourData> Hours { get; set; } = new();

    [JsonPropertyName("battery")]
    [JsonRequired]
    public Battery Battery { get; set; } = new();
}
