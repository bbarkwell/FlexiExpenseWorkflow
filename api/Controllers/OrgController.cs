using ExpenseWorkflow.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Controllers;

[ApiController]
[Route("api")]
public class OrgController(AppDbContext db) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> Users() =>
        Ok(await db.Users.OrderBy(u => u.Name).Select(u => new
        {
            u.Id,
            u.Name,
            u.Email,
            Department = u.Department!.Name,
            Manager = u.Manager!.Name,
            Roles = u.Roles.Select(r => r.Name)
        }).ToListAsync());

    [HttpGet("departments")]
    public async Task<IActionResult> Departments() =>
        Ok(await db.Departments.OrderBy(d => d.Name).Select(d => new { d.Id, d.Name, Head = d.HeadUser!.Name }).ToListAsync());
}
