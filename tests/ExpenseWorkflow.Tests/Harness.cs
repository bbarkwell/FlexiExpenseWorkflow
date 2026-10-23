using ExpenseWorkflow.Api.Data;
using ExpenseWorkflow.Api.Engine;
using ExpenseWorkflow.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Tests;

/// <summary>
/// A fresh in-memory SQLite database built from the real migrations and the dev seed data
/// (Eve -> Mark -> Dana; Mark is out of office with Mia as delegate; Frank holds Finance).
/// </summary>
public sealed class Harness : IDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext Db { get; }
    public WorkflowEngine Engine { get; }
    public DelegationManager Delegations { get; }
    public WorkflowAdminService Workflows { get; }
    public OrgAdminService Org { get; }

    private Harness(SqliteConnection connection, AppDbContext db)
    {
        _connection = connection;
        Db = db;
        IApproverResolver[] resolvers =
        [
            new ManagerResolver(), new DepartmentHeadResolver(db), new SpecificUserResolver(db), new RoleResolver(db)
        ];
        var delegationService = new DelegationService(db);
        Engine = new WorkflowEngine(db, TimeProvider.System, resolvers, delegationService);
        Delegations = new DelegationManager(db, TimeProvider.System, delegationService);
        Workflows = new WorkflowAdminService(db, TimeProvider.System, Engine, delegationService);
        Org = new OrgAdminService(db);
    }

    public static async Task<Harness> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
        return new Harness(connection, db);
    }

    public Task<User> UserAsync(string name) =>
        Db.Users.Include(u => u.Roles).SingleAsync(u => u.Email == $"{name.ToLower()}@acme.test");

    /// <summary>A draft report for the named user with one line per amount (in cents).</summary>
    public async Task<ExpenseReport> DraftAsync(string submitter, params long[] lineCents)
    {
        var user = await UserAsync(submitter);
        var report = new ExpenseReport { SubmitterId = user.Id, Submitter = user, Title = "Test" };
        foreach (var cents in lineCents)
            report.Lines.Add(new ExpenseLine { Date = DateOnly.FromDateTime(DateTime.UtcNow), Category = "Travel", AmountCents = cents });
        report.TotalCents = lineCents.Sum();
        Db.ExpenseReports.Add(report);
        await Db.SaveChangesAsync();
        return report;
    }

    public async Task<ExpenseReport> SubmitAsync(string submitter, params long[] lineCents)
    {
        var report = await DraftAsync(submitter, lineCents);
        await Engine.SubmitAsync(report);
        return report;
    }

    public async Task ClearDelegationsAsync()
    {
        Db.Delegations.RemoveRange(Db.Delegations);
        await Db.SaveChangesAsync();
    }

    public ApprovalTask Active(ExpenseReport report) =>
        report.Tasks.Single(t => t.Status == ApprovalTaskStatus.Active);

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
