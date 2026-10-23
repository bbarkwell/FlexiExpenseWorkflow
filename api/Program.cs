using ExpenseWorkflow.Api.Data;
using ExpenseWorkflow.Api.Engine;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(o => o.Filters.Add<EngineExceptionFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ExpenseWorkflow.Api.Services.CurrentUser>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IApproverResolver, ManagerResolver>();
builder.Services.AddScoped<IApproverResolver, DepartmentHeadResolver>();
builder.Services.AddScoped<IApproverResolver, SpecificUserResolver>();
builder.Services.AddScoped<IApproverResolver, RoleResolver>();
builder.Services.AddScoped<DelegationService>();
builder.Services.AddScoped<WorkflowEngine>();
builder.Services.AddScoped<DelegationManager>();
builder.Services.AddScoped<WorkflowAdminService>();
builder.Services.AddScoped<OrgAdminService>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await DbSeeder.SeedAsync(db);
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();
