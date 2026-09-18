using Microsoft.AspNetCore.Mvc;

namespace CampusGrid.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new { status = "ok" });
    }
}
