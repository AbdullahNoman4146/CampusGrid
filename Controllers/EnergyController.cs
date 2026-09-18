using Microsoft.AspNetCore.Mvc;
using CampusGrid.Models.Common;
using CampusGrid.Models.Dto;
using CampusGrid.LLM;
using CampusGrid.Services;
using CampusGrid.Validation;

namespace CampusGrid.Controllers;

[ApiController]
public class EnergyController : ControllerBase
{
    private readonly IEnergyService _energyService;
    private readonly ILogger<EnergyController> _logger;

    public EnergyController(IEnergyService energyService, ILogger<EnergyController> logger)
    {
        _energyService = energyService;
        _logger = logger;
    }

    [HttpPost("optimize-energy")]
    [ProducesResponseType(typeof(EnergyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> OptimizeEnergy([FromBody] EnergyRequest? request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(new ApiErrorResponse
            {
                Error = "Bad Request",
                Message = "Request body cannot be empty or invalid JSON.",
                StatusCode = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var response = await _energyService.OptimizeScenarioAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (InvalidSemanticInputException ex)
        {
            _logger.LogWarning("Semantic validation error: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new ApiErrorResponse
            {
                Error = "Unprocessable Entity",
                Message = ex.Message,
                Details = ex.ValidationErrors,
                StatusCode = StatusCodes.Status422UnprocessableEntity
            });
        }
        catch (LLMServiceUnavailableException ex)
        {
            _logger.LogError(ex, "LLM interpretation service is unavailable.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Error = "Internal Server Error",
                Message = "The language-model interpreter is temporarily unavailable.",
                StatusCode = StatusCodes.Status500InternalServerError
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected internal error during energy optimization.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Error = "Internal Server Error",
                Message = "An unexpected error occurred while processing the energy scenario.",
                StatusCode = StatusCodes.Status500InternalServerError
            });
        }
    }
}
