export interface UserSummary {
  id: number
  name: string
  email: string
  department: string | null
  manager: string | null
  roles: string[]
}

export interface Line {
  id: number
  date: string
  category: string
  amountCents: number
  description: string
}

export interface ApprovalTask {
  id: number
  sequence: number
  status: 'Pending' | 'Active' | 'Approved' | 'Rejected' | 'Skipped'
  assignedRole: string | null
  resolvedApprover: string | null
  effectiveApprover: string | null
  delegatedFrom: string | null
  decidedBy: string | null
  decidedAt: string | null
  comment: string | null
}

export type ReportStatus = 'Draft' | 'InReview' | 'Approved' | 'Rejected' | 'Withdrawn'

export interface Report {
  id: number
  submitterId: number
  submitterName: string
  title: string
  totalCents: number
  status: ReportStatus
  createdAt: string
  submittedAt: string | null
  lines: Line[]
  tasks: ApprovalTask[]
}

export interface SaveReportRequest {
  title: string
  lines: { date: string; category: string; amountCents: number; description: string }[]
}

export interface PendingApproval {
  taskId: number
  reportId: number
  title: string
  submitterName: string
  totalCents: number
  submittedAt: string | null
  sequence: number
  delegatedFrom: string | null
}

export interface Delegation {
  id: number
  delegateUserId: number
  delegateName: string
  startsOn: string
  endsOn: string
}

export type ApproverType = 'Manager' | 'DepartmentHead' | 'Role' | 'SpecificUser'

export interface Step {
  approverType: ApproverType
  approverParam: string | null
}

export interface Rule {
  name: string
  condition: unknown | null
  steps: Step[]
}

export interface Workflow {
  id: number
  name: string
  version: number
  status: 'Draft' | 'Published' | 'Retired'
  effectiveFrom: string | null
  isCurrent: boolean
  rules: Rule[]
}

export interface Validation {
  errors: string[]
  warnings: string[]
}

export interface SimulatedStep {
  sequence: number
  approverType: ApproverType
  approvers: string[]
  skipped: boolean
  skipReason: string | null
  actingToday: string | null
}

export interface Simulation {
  ruleName: string
  rulePriority: number
  steps: SimulatedStep[]
}

export interface AdminUser {
  id: number
  name: string
  email: string
  department: string | null
  roles: string[]
}
