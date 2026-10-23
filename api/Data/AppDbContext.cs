using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Delegation> Delegations => Set<Delegation>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowRule> WorkflowRules => Set<WorkflowRule>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<ExpenseReport> ExpenseReports => Set<ExpenseReport>();
    public DbSet<ExpenseLine> ExpenseLines => Set<ExpenseLine>();
    public DbSet<ApprovalTask> ApprovalTasks => Set<ApprovalTask>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Role>().HasIndex(r => r.Name).IsUnique();
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();

        b.Entity<User>().HasMany(u => u.Roles).WithMany(r => r.Users);
        b.Entity<User>().HasOne(u => u.Manager).WithMany().HasForeignKey(u => u.ManagerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<User>().HasOne(u => u.Department).WithMany().HasForeignKey(u => u.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Department>().HasOne(d => d.HeadUser).WithMany().HasForeignKey(d => d.HeadUserId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Delegation>().HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Delegation>().HasOne(d => d.DelegateUser).WithMany().HasForeignKey(d => d.DelegateUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Delegation>().HasIndex(d => new { d.UserId, d.StartsOn, d.EndsOn });

        b.Entity<WorkflowDefinition>().Property(w => w.Status).HasConversion<string>();
        b.Entity<WorkflowDefinition>().HasIndex(w => new { w.Name, w.Version }).IsUnique();
        b.Entity<WorkflowRule>().HasOne(r => r.Definition).WithMany(d => d.Rules).HasForeignKey(r => r.DefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkflowStep>().Property(s => s.ApproverType).HasConversion<string>();
        b.Entity<WorkflowStep>().HasOne(s => s.Rule).WithMany(r => r.Steps).HasForeignKey(s => s.RuleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkflowStep>().HasIndex(s => new { s.RuleId, s.Sequence }).IsUnique();

        b.Entity<ExpenseReport>().Property(r => r.Title).HasMaxLength(200);
        b.Entity<ExpenseReport>().Property(r => r.Status).HasConversion<string>();
        b.Entity<ExpenseReport>().Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
        b.Entity<ExpenseReport>().HasOne(r => r.Submitter).WithMany().HasForeignKey(r => r.SubmitterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExpenseReport>().HasOne(r => r.WorkflowDefinition).WithMany().HasForeignKey(r => r.WorkflowDefinitionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExpenseReport>().HasOne(r => r.MatchedRule).WithMany().HasForeignKey(r => r.MatchedRuleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExpenseLine>().HasOne(l => l.Report).WithMany(r => r.Lines).HasForeignKey(l => l.ReportId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ApprovalTask>().Property(t => t.Status).HasConversion<string>();
        b.Entity<ApprovalTask>().Property(t => t.ConcurrencyStamp).IsConcurrencyToken();
        b.Entity<ApprovalTask>().HasOne(t => t.Report).WithMany(r => r.Tasks).HasForeignKey(t => t.ReportId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ApprovalTask>().HasOne(t => t.Step).WithMany().HasForeignKey(t => t.StepId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<ApprovalTask>().HasOne(t => t.ResolvedApprover).WithMany().HasForeignKey(t => t.ResolvedApproverId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApprovalTask>().HasOne(t => t.EffectiveApprover).WithMany().HasForeignKey(t => t.EffectiveApproverId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApprovalTask>().HasOne(t => t.DelegatedFrom).WithMany().HasForeignKey(t => t.DelegatedFromId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApprovalTask>().HasOne(t => t.AssignedRole).WithMany().HasForeignKey(t => t.AssignedRoleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApprovalTask>().HasOne(t => t.DecidedBy).WithMany().HasForeignKey(t => t.DecidedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApprovalTask>().HasIndex(t => new { t.EffectiveApproverId, t.Status });
        b.Entity<ApprovalTask>().HasIndex(t => new { t.ReportId, t.Sequence }).IsUnique();

        b.Entity<AuditEvent>().HasOne(a => a.Report).WithMany().HasForeignKey(a => a.ReportId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AuditEvent>().HasOne(a => a.Actor).WithMany().HasForeignKey(a => a.ActorId).OnDelete(DeleteBehavior.Restrict);
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var e in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            if (e.Entity is ExpenseReport r) r.ConcurrencyStamp = Guid.NewGuid();
            else if (e.Entity is ApprovalTask t) t.ConcurrencyStamp = Guid.NewGuid();
        }
        return base.SaveChangesAsync(ct);
    }
}
