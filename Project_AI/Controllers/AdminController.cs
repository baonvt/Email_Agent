using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project_AI.Domain.Constants;


namespace Project_AI.API.Controllers;

[ApiController, Route("api/admin"), Authorize(Roles = AuthRoles.Admin)]
public sealed class AdminController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { message = "Admin access granted." });
}
