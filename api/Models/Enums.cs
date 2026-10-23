namespace ExpenseWorkflow.Api.Models;

public enum ReportStatus { Draft, InReview, Approved, Rejected, Withdrawn }

public enum WorkflowStatus { Draft, Published, Retired }

public enum ApproverType { Manager, DepartmentHead, Role, SpecificUser }

public enum ApprovalTaskStatus { Pending, Active, Approved, Rejected, Skipped }
