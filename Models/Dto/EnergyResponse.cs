using System.Text.Json.Serialization;

namespace CampusGrid.Models.Dto;

public class EnergyResponse
{
    [JsonPropertyName("scenario_id")]
    public string ScenarioId { get; set; } = string.Empty;

    [JsonPropertyName("directive_interpretation")]
    public List<DirectiveInterpretationDto> DirectiveInterpretation { get; set; } = new();

    [JsonPropertyName("hourly_plan")]
    public List<HourlyPlanDto> HourlyPlan { get; set; } = new();

    [JsonPropertyName("total_grid_kwh")]
    public double TotalGridKwh { get; set; }

    [JsonPropertyName("total_cost_bdt")]
    public double TotalCostBdt { get; set; }

    [JsonPropertyName("peak_grid_kwh")]
    public double PeakGridKwh { get; set; }

    [JsonPropertyName("plan_summary")]
    public string PlanSummary { get; set; } = string.Empty;
}
