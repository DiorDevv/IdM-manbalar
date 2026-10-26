// IdM API kontraktlari (Bank.Idm.Api/Endpoints/Contracts.cs bilan mos).
export type IdentityStatus = 'Pending' | 'Active' | 'Suspended' | 'Terminated'
export type IdentitySource = 'Manual' | 'Hr' | 'ActiveDirectory'
export type AccountStatus = 'Enabled' | 'Disabled' | 'Deleted'
export type RiskLevel = 'Low' | 'Medium' | 'High' | 'Critical'
export type RunStatus = 'Running' | 'Succeeded' | 'Failed' | 'Aborted'

export interface PagedResult<T> { items: T[]; total: number; skip: number; take: number }
export interface Me { login: string; name: string | null; roles: string[]; identityId: string | null; hasTeam: boolean; inboxCount: number; reviewCount: number }
export interface DashboardSummary {
  identities: number; activeIdentities: number; suspendedIdentities: number; terminatedIdentities: number
  applications: number; accounts: number; disabledAccounts: number; orphanAccounts: number; leaversWithActiveAccounts: number; sodViolations: number
  pendingRequests: number; lastAuditAt: string | null
  connectors: ConnectorStatus[]
}
export interface ConnectorStatus { code: string; name: string; kind: string; isRunning: boolean; lastRun: ConnectorRun | null; lastSuccessAt: string | null }
export interface ConnectorRun {
  id: string; trigger: 'Schedule' | 'Manual'; triggeredBy: string; status: RunStatus; startedAt: string; finishedAt: string | null
  accountsRead: number; groupsRead: number; accountsCreated: number; accountsUpdated: number; accountsDisabled: number; accountsDeleted: number
  membershipsAdded: number; membershipsRemoved: number; identitiesCreated: number; identitiesUpdated: number; identitiesTerminated: number
  identitiesMissing: number; orphanAccounts: number; error: string | null
}
export interface IdentityListItem {
  id: string; employeeNumber: string; displayName: string; email: string | null; department: string | null
  jobTitle: string | null; status: IdentityStatus; source: IdentitySource; accountCount: number; updatedAt: string
}
export interface EntitlementRef { id: string; name: string; isPrivileged: boolean }
export interface AccountItem { id: string; applicationCode: string; applicationName: string; login: string; status: AccountStatus; kind: string; lastSyncedAt: string | null; entitlements: EntitlementRef[] }
export interface IdentityDetails {
  id: string; employeeNumber: string; firstName: string; lastName: string; middleName: string | null; email: string | null
  department: string | null; jobTitle: string | null; managerId: string | null; managerName: string | null
  status: IdentityStatus; source: IdentitySource; hireDate: string | null; terminationDate: string | null
  createdAt: string; updatedAt: string; accounts: AccountItem[]
}
export interface ApplicationItem {
  id: string; code: string; name: string; description: string | null; kind: string; risk: RiskLevel; isActive: boolean
  accountCount: number; entitlementCount: number; hasConnector: boolean; isSyncing: boolean; lastRun: ConnectorRun | null
}
export interface ApplicationDetails {
  id: string; code: string; name: string; description: string | null; kind: string; risk: RiskLevel; isActive: boolean
  enabledAccounts: number; disabledAccounts: number; deletedAccounts: number; orphanAccounts: number; entitlements: number; privilegedEntitlements: number
  hasConnector: boolean; isSyncing: boolean; intervalMinutes: number | null; lastRun: ConnectorRun | null; lastSuccessAt: string | null
}
export interface ApplicationAccount {
  id: string; login: string; displayName: string | null; email: string | null; employeeNumber: string | null; status: AccountStatus; kind: string
  identityId: string | null; identityName: string | null; entitlementCount: number; lastSyncedAt: string | null
}
export interface EntitlementItem { id: string; name: string; description: string | null; risk: RiskLevel; isPrivileged: boolean; memberCount: number; deletedAt: string | null; lastSyncedAt: string | null }
export interface AuditItem { id: number; occurredAt: string; actorId: string; actorName: string | null; action: string; targetType: string | null; targetId: string | null; details: string | null; ipAddress: string | null; correlationId: string | null }
export interface AuditVerification { isValid: boolean; checkedCount: number; firstBrokenId: number | null }
export type EventKind = 'Joiner' | 'Mover' | 'Leaver' | 'Rehire' | 'Suspended' | 'Resumed'
export interface CorrelationSummary { orphanAccounts: number; leaversWithActiveAccounts: number; identitiesWithoutAccounts: number; notInHr: number; hrConnected: boolean }
export interface OrphanAccountItem { id: string; applicationCode: string; applicationName: string; login: string; displayName: string | null; employeeNumber: string | null; status: AccountStatus; lastSyncedAt: string | null }
export interface LeaverItem { id: string; employeeNumber: string; displayName: string; department: string | null; terminationDate: string | null; activeAccounts: AccountItem[] }
export interface IdentityEventItem { id: string; kind: EventKind; occurredAt: string; source: string; identityId: string; identityName: string; employeeNumber: string; department: string | null; details: string | null }

// ---------- Rollar va SoD ----------
export type AssignmentSource = 'Birthright' | 'Request' | 'Manual'
export type AccessState = 'Granted' | 'Missing' | 'Excess'
export interface EntitlementPick { id: string; applicationCode: string; name: string; isPrivileged: boolean; risk: RiskLevel }
export interface RoleListItem { id: string; code: string; name: string; description: string | null; risk: RiskLevel; isRequestable: boolean; entitlementCount: number; memberCount: number; ruleCount: number }
export interface RoleRuleItem { id: string; department: string | null; jobTitle: string | null; matchCount: number; createdAt: string; createdBy: string }
export interface RoleMemberItem { identityId: string; name: string; employeeNumber: string; department: string | null; status: IdentityStatus; source: AssignmentSource; validFrom: string }
export interface RoleDetails {
  id: string; code: string; name: string; description: string | null; risk: RiskLevel; isRequestable: boolean; createdAt: string
  entitlements: EntitlementPick[]; rules: RoleRuleItem[]; members: RoleMemberItem[]; sodConflicts: string[]
}
export interface SaveRole { code: string; name: string; description: string | null; risk: RiskLevel; isRequestable: boolean }
export interface AttributeValues { departments: string[]; jobTitles: string[] }
export interface BirthrightResult { assigned: number; removed: number; identitiesChecked: number }
export interface AccessLine { entitlementId: string; applicationCode: string; entitlementName: string; isPrivileged: boolean; state: AccessState; viaRoles: string[]; accounts: string[] }
export interface IdentityRoleItem { roleId: string; code: string; name: string; source: AssignmentSource; validFrom: string }
export interface SodViolationItem { ruleId: string; ruleCode: string; ruleName: string; risk: RiskLevel; identityId: string; identityName: string; employeeNumber: string; department: string | null; left: string[]; right: string[] }
export interface IdentityAccess { roles: IdentityRoleItem[]; lines: AccessLine[]; sod: SodViolationItem[] }
export interface SodRuleItem { id: string; code: string; name: string; description: string | null; risk: RiskLevel; left: EntitlementPick[]; right: EntitlementPick[]; violationCount: number; createdAt: string; createdBy: string }
export interface DriftItem { identityId: string; name: string; employeeNumber: string; department: string | null; status: IdentityStatus; missing: number; excess: number }

// ---------- Arizalar ----------
export type RequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled' | 'Fulfilled' | 'Failed'
export type StepKind = 'Manager' | 'ResourceOwner' | 'Security'
export type Decision = 'Pending' | 'Approved' | 'Rejected'
export interface CatalogRole { id: string; code: string; name: string; description: string | null; risk: RiskLevel; entitlements: string[] }
export interface PersonBrief { id: string; name: string; employeeNumber: string; department: string | null; jobTitle: string | null }
export interface SodConflictInfo { ruleCode: string; ruleName: string; risk: string; left: string[]; right: string[] }
export interface RequestPreview { canSubmit: boolean; blocker: string | null; steps: { kind: StepKind; approverName: string | null; auto: boolean }[]; sod: SodConflictInfo[] }
export interface RequestListItem {
  id: string; status: RequestStatus; roleId: string; roleCode: string; roleName: string; risk: RiskLevel; beneficiaryId: string; beneficiaryName: string
  beneficiaryNumber: string; requesterName: string; createdAt: string; closedAt: string | null; currentStep: StepKind | null; currentApprover: string | null; hasSod: boolean
}
export interface ApprovalItem { step: number; kind: StepKind; approverId: string | null; approverName: string | null; decision: Decision; comment: string | null; decidedAt: string | null; decidedBy: string | null; decidedByName: string | null }
export interface RequestDetails {
  id: string; status: RequestStatus; roleId: string; roleCode: string; roleName: string; roleDescription: string | null; risk: RiskLevel; entitlements: string[]
  beneficiary: PersonBrief; requester: PersonBrief; justification: string; validTo: string | null; createdAt: string; closedAt: string | null
  approvals: ApprovalItem[]; sodConflicts: SodConflictInfo[]; canDecide: boolean; canCancel: boolean
}

export type ProvisioningKind = 'CreateAccount' | 'DisableAccount' | 'AddMembership' | 'RemoveMembership'
export type ProvisioningStatus = 'Proposed' | 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Obsolete' | 'Cancelled'
export type ProvisioningMode = 'Off' | 'Approval' | 'Automatic'
export interface ProvisioningOperationItem {
  id: string; applicationCode: string; kind: ProvisioningKind; status: ProvisioningStatus; reason: string
  identityId: string | null; identityName: string | null; employeeNumber: string | null; accountId: string | null; login: string | null
  group: string | null; groupPrivileged: boolean; summary: string | null; createdAt: string; approvedBy: string | null; approvedByName: string | null
  approvedAt: string | null; attempts: number; nextAttemptAt: string | null; lastError: string | null; completedAt: string | null
}
export interface ProvisioningApplicationStatus {
  code: string; name: string; mode: ProvisioningMode; createAccounts: boolean; maxAutoOperations: number
  proposed: number; queued: number; running: number; failed: number; succeededLast24h: number
}
export interface PlanResult { planned: number; created: number; obsoleted: number; held: boolean; skipped: string | null; warnings: string[] }
export interface ProvisioningActionResult { changed: number; skipped: number; error: string | null }

export type ReviewStatus = 'Active' | 'Completed' | 'Cancelled'
export type ReviewDecision = 'Pending' | 'Keep' | 'Revoke'
export type ReviewItemKind = 'Role' | 'Entitlement'
export interface CampaignBody { name: string; description: string | null; department: string | null; applicationId: string | null; privilegedOnly: boolean; dueDate: string }
export interface CampaignPreview { items: number; roles: number; entitlements: number; privileged: number; identities: number; reviewers: number; adminPool: number }
export interface CampaignItem {
  id: string; name: string; description: string | null; status: ReviewStatus; department: string | null; applicationCode: string | null; privilegedOnly: boolean
  dueDate: string; createdAt: string; createdBy: string; createdByName: string | null; closedAt: string | null
  total: number; kept: number; revoked: number; pending: number; revokedOpen: number
}
export interface CampaignDetails { campaign: CampaignItem; reviewers: { reviewerId: string | null; reviewerName: string | null; total: number; pending: number }[] }
export interface ReviewItem {
  id: string; campaignId: string; campaignName: string; dueDate: string; kind: ReviewItemKind; identityId: string; identityName: string; employeeNumber: string
  department: string | null; jobTitle: string | null; identityStatus: IdentityStatus; applicationCode: string; title: string; subtitle: string | null
  isPrivileged: boolean; reviewerId: string | null; reviewerName: string | null; decision: ReviewDecision; comment: string | null; decidedAt: string | null
  decidedBy: string | null; decidedByName: string | null; remediatedAt: string | null
}
export type ReportKey = 'access' | 'privileged' | 'sod' | 'orphans' | 'leavers' | 'campaign'
export interface ReportTable { key: ReportKey; title: string; subtitle: string | null; columns: string[]; rows: (string | null)[][]; generatedAt: string }
