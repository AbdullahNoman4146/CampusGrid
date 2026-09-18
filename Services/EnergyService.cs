using CampusGrid.LLM;
using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;
using CampusGrid.Optimization;
using CampusGrid.Validation;

namespace CampusGrid.Services;

public class EnergyService : IEnergyService
{
    private readonly IEnergyRequestValidator _requestValidator;
    private readonly ILLMInterpreter _llmInterpreter;
    private readonly IDirectiveValidator _directiveValidator;
    private readonly IEnergyOptimizer _energyOptimizer;
    private readonly IScheduleValidator _scheduleValidator;
    private readonly ILogger<EnergyService> _logger;

    public EnergyService(
        IEnergyRequestValidator requestValidator,
        ILLMInterpreter llmInterpreter,
        IDirectiveValidator directiveValidator,
        IEnergyOptimizer energyOptimizer,
        IScheduleValidator scheduleValidator,
        ILogger<EnergyService> logger)
    {
        _requestValidator = requestValidator;
        _llmInterpreter = llmInterpreter;
        _directiveValidator = directiveValidator;
        _energyOptimizer = energyOptimizer;
        _scheduleValidator = scheduleValidator;
        _logger = logger;
    }

    public async Task<EnergyResponse> OptimizeScenarioAsync(EnergyRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Validate Input JSON and semantic constraints
        _requestValidator.Validate(request);

        // 2. Interpret operator notes via LLM (with fallback) and validate through guardrails
        var directives = new List<DirectiveInterpretationDto>();
        for (int i = 0; i < request.OperatorNotes.Count; i++)
        {
            var note = request.OperatorNotes[i];
            var interpretation = await _llmInterpreter.InterpretAsync(note, i, request.Battery, cancellationToken);

            // Guardrail validation of untrusted LLM output
            _directiveValidator.Validate(interpretation, request.Battery);

            directives.Add(interpretation);
        }

        // 3. Formulate and solve the mathematical optimization model
        var optResult = _energyOptimizer.Optimize(request, directives);
        if (!optResult.Success)
        {
            _logger.LogWarning("Optimization failed for scenario {ScenarioId}: {Error}", request.ScenarioId, optResult.ErrorMessage);
            throw new InfeasibleScenarioException(optResult.ErrorMessage ?? "The energy scenario is infeasible under current constraints.");
        }

        // 4. Construct response DTO
        var response = new EnergyResponse
        {
            ScenarioId = request.ScenarioId,
            DirectiveInterpretation = directives,
            HourlyPlan = optResult.HourlyPlan,
            TotalGridKwh = optResult.TotalGridKwh,
            TotalCostBdt = optResult.TotalCostBdt,
            PeakGridKwh = optResult.PeakGridKwh,
            PlanSummary = GeneratePlanSummary(request, optResult, directives)
        };

        // 5. Final deterministic schedule validation
        _scheduleValidator.Validate(response, request, directives);

        return response;
    }

    private static string GeneratePlanSummary(
        EnergyRequest request,
        OptimizationResult result,
        List<DirectiveInterpretationDto> directives)
    {
        var appliedCount = directives.Count(d => d.Applies);
        var notesSummary = appliedCount > 0
            ? $"Satisfies {appliedCount} active directive(s)"
            : "No operational directives applied";

        return $"Optimized 24-hour campus energy schedule for scenario {request.ScenarioId}. " +
               $"Total grid import: {result.TotalGridKwh:F1} kWh, total cost: {result.TotalCostBdt:F0} BDT, peak grid import: {result.PeakGridKwh:F1} kWh. " +
               $"{notesSummary} and maintains end-of-day battery neutrality.";
    }
}
