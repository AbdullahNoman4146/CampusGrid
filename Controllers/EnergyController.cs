using Microsoft.AspNetCore.Mvc;
using CampusGrid.Models.Common;
using CampusGrid.Models.Dto;
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

    [HttpGet("optimize-energy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetOptimizeEnergyInfo()
    {
        return Ok(new
        {
            status = "ready",
            endpoint = "/optimize-energy",
            accepted_method = "POST",
            message = "This endpoint requires an HTTP POST request with a JSON scenario body. Use the interactive Swagger UI or curl to test.",
            interactive_docs = "/swagger",
            sample_post_curl = "curl.exe -k -X POST https://localhost:7116/optimize-energy -H \"Content-Type: application/json\" -d @scenario.json"
        });
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
