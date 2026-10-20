// IdM API kontraktlari (Bank.Idm.Api/Endpoints/Contracts.cs bilan mos).
export type IdentityStatus = 'Pending' | 'Active' | 'Suspended' | 'Terminated'
export type IdentitySource = 'Manual' | 'Hr' | 'ActiveDirectory'
export type AccountStatus = 'Enabled' | 'Disabled' | 'Deleted'
export type RiskLevel = 'Low' | 'Medium' | 'High' | 'Critical'

export interface PagedResult<T> { items: T[]; total: number; skip: number; take: number }
export interface Me { login: string; name: string | null; roles: string[]; identityId: string | null }
export interface DashboardSummary {
  identities: number; activeIdentities: number; suspendedIdentities: number; terminatedIdentities: number
  applications: number; accounts: number; orphanAccounts: number; pendingRequests: number; lastAuditAt: string | null
}
export interface IdentityListItem {
  id: string; employeeNumber: string; displayName: string; email: string | null; department: string | null
  jobTitle: string | null; status: IdentityStatus; source: IdentitySource; accountCount: number; updatedAt: string
}
export interface AccountItem { id: string; applicationCode: string; applicationName: string; login: string; status: AccountStatus; kind: string; lastSyncedAt: string | null }
export interface IdentityDetails {
  id: string; employeeNumber: string; firstName: string; lastName: string; middleName: string | null; email: string | null
  department: string | null; jobTitle: string | null; managerId: string | null; managerName: string | null
  status: IdentityStatus; source: IdentitySource; hireDate: string | null; terminationDate: string | null
  createdAt: string; updatedAt: string; accounts: AccountItem[]
}
export interface ApplicationItem { id: string; code: string; name: string; description: string | null; kind: string; risk: RiskLevel; isActive: boolean; accountCount: number; entitlementCount: number }
export interface AuditItem { id: number; occurredAt: string; actorId: string; actorName: string | null; action: string; targetType: string | null; targetId: string | null; details: string | null; ipAddress: string | null; correlationId: string | null }
export interface AuditVerification { isValid: boolean; checkedCount: number; firstBrokenId: number | null }
