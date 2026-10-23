using ExpenseWorkflow.Api.Data;
using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Services;

/// <summary>
/// Development stand-in until authentication exists: the acting user comes from the
/// X-User-Id request header. Replace the body of this class when real auth is added.
/// </summary>
public class CurrentUser(IHttpContextAccessor http, AppDbContext db)
{
    public async Task<User?> GetAsync()
    {
        var header = http.HttpContext?.Request.Headers["X-User-Id"].ToString();
        return int.TryParse(header, out var id)
            ? await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id)
            : null;
    }
}
