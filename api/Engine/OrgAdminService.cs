using ExpenseWorkflow.Api.Data;
using ExpenseWorkflow.Api.Dtos;
using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Engine;

/// <summary>Org chart maintenance: users, departments and roles. Guards against reporting-line loops.</summary>
public class OrgAdminService(AppDbContext db)
{
    private IQueryable<User> UsersQuery => db.Users.Include(u => u.Roles).Include(u => u.Department).Include(u => u.Manager);

    public Task<List<User>> ListUsersAsync() => UsersQuery.OrderBy(u => u.Name).ToListAsync();

    public Task<List<Department>> ListDepartmentsAsync() => db.Departments.Include(d => d.HeadUser).OrderBy(d => d.Name).ToListAsync();

    public Task<List<Role>> ListRolesAsync() => db.Roles.OrderBy(r => r.Name).ToListAsync();

    public async Task<Role> CreateRoleAsync(string name)
    {
        name = name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == name))
            throw new EngineException(EngineError.Conflict, $"The role '{name}' already exists.");
        var role = new Role { Name = name };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role;
    }

    public async Task<User> CreateUserAsync(UserRequest request)
    {
        var user = new User();
        await ApplyAsync(user, request);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return await GetUserAsync(user.Id);
    }

    public async Task<User> UpdateUserAsync(int id, UserRequest request)
    {
        var user = await UsersQuery.FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new EngineException(EngineError.NotFound, "User not found.");
        await ApplyAsync(user, request);
        await db.SaveChangesAsync();
        return await GetUserAsync(id);
    }

    public async Task<Department> CreateDepartmentAsync(DepartmentRequest request)
    {
        var dept = new Department();
        await ApplyAsync(dept, request);
        db.Departments.Add(dept);
        await db.SaveChangesAsync();
        return await GetDepartmentAsync(dept.Id);
    }

    public async Task<Department> UpdateDepartmentAsync(int id, DepartmentRequest request)
    {
        var dept = await db.Departments.FindAsync(id) ?? throw new EngineException(EngineError.NotFound, "Department not found.");
        await ApplyAsync(dept, request);
        await db.SaveChangesAsync();
        return await GetDepartmentAsync(id);
    }

    private async Task<User> GetUserAsync(int id) => await UsersQuery.AsNoTracking().FirstAsync(u => u.Id == id);

    private async Task<Department> GetDepartmentAsync(int id) =>
        await db.Departments.Include(d => d.HeadUser).AsNoTracking().FirstAsync(d => d.Id == id);

    private async Task ApplyAsync(User user, UserRequest request)
    {
        var email = request.Email.Trim();
        if (await db.Users.AnyAsync(u => u.Email == email && u.Id != user.Id))
            throw new EngineException(EngineError.Conflict, $"Someone with the email '{email}' already exists.");
        if (request.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d))
            throw new EngineException(EngineError.Invalid, "The department doesn't exist.");
        if (request.ManagerId is { } m)
        {
            if (!await db.Users.AnyAsync(x => x.Id == m))
                throw new EngineException(EngineError.Invalid, "The manager doesn't exist.");
            if (user.Id != 0 && await ReportsUpToAsync(m, user.Id))
                throw new EngineException(EngineError.Invalid, "That would create a reporting loop: the manager already reports to this person.");
        }

        var names = (request.Roles ?? []).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var roles = await db.Roles.Where(r => names.Contains(r.Name)).ToListAsync();
        var missing = names.Except(roles.Select(r => r.Name), StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0)
            throw new EngineException(EngineError.Invalid, $"Unknown role(s): {string.Join(", ", missing)}.");

        user.Name = request.Name.Trim();
        user.Email = email;
        user.DepartmentId = request.DepartmentId;
        user.ManagerId = request.ManagerId;
        user.Roles.Clear();
        user.Roles.AddRange(roles);
    }

    private async Task ApplyAsync(Department dept, DepartmentRequest request)
    {
        if (request.HeadUserId is { } h && !await db.Users.AnyAsync(u => u.Id == h))
            throw new EngineException(EngineError.Invalid, "The department head doesn't exist.");
        dept.Name = request.Name.Trim();
        dept.HeadUserId = request.HeadUserId;
    }

    /// <summary>True if walking up from <paramref name="startId"/> through managers reaches <paramref name="targetId"/> (or loops).</summary>
    private async Task<bool> ReportsUpToAsync(int startId, int targetId)
    {
        var seen = new HashSet<int>();
        int? current = startId;
        while (current is { } id && seen.Add(id))
        {
            if (id == targetId) return true;
            current = await db.Users.Where(u => u.Id == id).Select(u => u.ManagerId).FirstOrDefaultAsync();
        }
        return false;
    }
}
