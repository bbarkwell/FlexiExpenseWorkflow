using ExpenseWorkflow.Api.Dtos;
using ExpenseWorkflow.Api.Engine;
using ExpenseWorkflow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseWorkflow.Api.Controllers;

[ApiController]
[RequireAdmin]
[Route("api/admin")]
public class AdminOrgController(OrgAdminService org) : ControllerBase
{
    [HttpGet("users")]
    public async Task<List<AdminUserDto>> Users() => (await org.ListUsersAsync()).Select(AdminUserDto.From).ToList();

    [HttpPost("users")]
    public async Task<ActionResult<AdminUserDto>> CreateUser(UserRequest request) =>
        StatusCode(StatusCodes.Status201Created, AdminUserDto.From(await org.CreateUserAsync(request)));

    [HttpPut("users/{id:int}")]
    public async Task<AdminUserDto> UpdateUser(int id, UserRequest request) =>
        AdminUserDto.From(await org.UpdateUserAsync(id, request));

    [HttpGet("departments")]
    public async Task<List<AdminDepartmentDto>> Departments() =>
        (await org.ListDepartmentsAsync()).Select(AdminDepartmentDto.From).ToList();

    [HttpPost("departments")]
    public async Task<ActionResult<AdminDepartmentDto>> CreateDepartment(DepartmentRequest request) =>
        StatusCode(StatusCodes.Status201Created, AdminDepartmentDto.From(await org.CreateDepartmentAsync(request)));

    [HttpPut("departments/{id:int}")]
    public async Task<AdminDepartmentDto> UpdateDepartment(int id, DepartmentRequest request) =>
        AdminDepartmentDto.From(await org.UpdateDepartmentAsync(id, request));

    [HttpGet("roles")]
    public async Task<List<string>> Roles() => (await org.ListRolesAsync()).Select(r => r.Name).ToList();

    [HttpPost("roles")]
    public async Task<ActionResult<string>> CreateRole(CreateRoleRequest request) =>
        StatusCode(StatusCodes.Status201Created, (await org.CreateRoleAsync(request.Name)).Name);
}
