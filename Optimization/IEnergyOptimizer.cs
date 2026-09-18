using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;

namespace CampusGrid.Optimization;

public interface IEnergyOptimizer
{
    OptimizationResult Optimize(EnergyRequest request, IEnumerable<DirectiveInterpretationDto> directives);
}

public class OptimizationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<HourlyPlanDto> HourlyPlan { get; set; } = new();
    public double TotalGridKwh { get; set; }
    public double TotalCostBdt { get; set; }
    public double PeakGridKwh { get; set; }
}
