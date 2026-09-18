using System.Text.Json.Serialization;
using CampusGrid.Models.Domain;

namespace CampusGrid.Models.Dto;

public class EnergyRequest
{
    [JsonPropertyName("scenario_id")]
    public string ScenarioId { get; set; } = string.Empty;

    [JsonPropertyName("operator_notes")]
    public List<string> OperatorNotes { get; set; } = new();

    [JsonPropertyName("hours")]
    public List<HourData> Hours { get; set; } = new();

    [JsonPropertyName("battery")]
    public Battery Battery { get; set; } = new();
}
