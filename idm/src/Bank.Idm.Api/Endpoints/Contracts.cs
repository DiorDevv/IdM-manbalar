using Bank.Idm.Domain;

namespace Bank.Idm.Api.Endpoints;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);

public record MeResponse(string Login, string? Name, IReadOnlyList<string> Roles, Guid? IdentityId);

public record DashboardSummary(
    int Identities, int ActiveIdentities, int SuspendedIdentities, int TerminatedIdentities,
    int Applications, int Accounts, int DisabledAccounts, int OrphanAccounts, int LeaversWithActiveAccounts, int SodViolations, int PendingRequests,
    DateTimeOffset? LastAuditAt, IReadOnlyList<ConnectorStatus> Connectors);

/// <summary>Tizim konnektorining holati: oxirgi ishga tushish va oxirgi muvaffaqiyatli sinxronlash.</summary>
public record ConnectorStatus(string Code, string Name, ApplicationKind Kind, bool IsRunning, ConnectorRunItem? LastRun, DateTimeOffset? LastSuccessAt);

public record IdentityListItem(Guid Id, string EmployeeNumber, string DisplayName, string? Email, string? Department,
    string? JobTitle, IdentityStatus Status, IdentitySource Source, int AccountCount, DateTimeOffset UpdatedAt);

public record IdentityDetails(Guid Id, string EmployeeNumber, string FirstName, string LastName, string? MiddleName,
    string? Email, string? Department, string? JobTitle, Guid? ManagerId, string? ManagerName, IdentityStatus Status,
    IdentitySource Source, DateOnly? HireDate, DateOnly? TerminationDate, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<AccountItem> Accounts);

public record AccountItem(Guid Id, string ApplicationCode, string ApplicationName, string Login, AccountStatus Status, AccountKind Kind,
    DateTimeOffset? LastSyncedAt, IReadOnlyList<EntitlementRef> Entitlements);

public record EntitlementRef(Guid Id, string Name, bool IsPrivileged);

public record CreateIdentityRequest(string EmployeeNumber, string FirstName, string LastName, string? MiddleName,
    string? Email, string? Department, string? JobTitle, DateOnly? HireDate);

public record ApplicationItem(Guid Id, string Code, string Name, string? Description, ApplicationKind Kind, RiskLevel Risk,
    bool IsActive, int AccountCount, int EntitlementCount, bool HasConnector, bool IsSyncing, ConnectorRunItem? LastRun);

public record ApplicationDetails(Guid Id, string Code, string Name, string? Description, ApplicationKind Kind, RiskLevel Risk, bool IsActive,
    int EnabledAccounts, int DisabledAccounts, int DeletedAccounts, int OrphanAccounts, int Entitlements, int PrivilegedEntitlements,
    bool HasConnector, bool IsSyncing, int? IntervalMinutes, ConnectorRunItem? LastRun, DateTimeOffset? LastSuccessAt);

public record ConnectorRunItem(Guid Id, ConnectorRunTrigger Trigger, string TriggeredBy, ConnectorRunStatus Status, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, int AccountsRead, int GroupsRead, int AccountsCreated, int AccountsUpdated, int AccountsDisabled,
    int AccountsDeleted, int MembershipsAdded, int MembershipsRemoved, int IdentitiesCreated, int IdentitiesUpdated, int IdentitiesTerminated,
    int IdentitiesMissing, int OrphanAccounts, string? Error);

public record SyncStarted(Guid RunId);

public record ApplicationAccountItem(Guid Id, string Login, string? DisplayName, string? Email, string? EmployeeNumber, AccountStatus Status,
    AccountKind Kind, Guid? IdentityId, string? IdentityName, int EntitlementCount, DateTimeOffset? LastSyncedAt);

public record EntitlementItem(Guid Id, string Name, string? Description, RiskLevel Risk, bool IsPrivileged, int MemberCount,
    DateTimeOffset? DeletedAt, DateTimeOffset? LastSyncedAt);

public record AuditItem(long Id, DateTimeOffset OccurredAt, string ActorId, string? ActorName, string Action,
    string? TargetType, string? TargetId, string? Details, string? IpAddress, string? CorrelationId);

/// <summary>Korrelyatsiya hisobotlari: HR (xodimlar) va tizimlardagi akkauntlar o'rtasidagi nomuvofiqliklar.</summary>
public record CorrelationSummary(int OrphanAccounts, int LeaversWithActiveAccounts, int IdentitiesWithoutAccounts, int NotInHr, bool HrConnected);

public record OrphanAccountItem(Guid Id, string ApplicationCode, string ApplicationName, string Login, string? DisplayName, string? EmployeeNumber,
    AccountStatus Status, DateTimeOffset? LastSyncedAt);

public record LeaverItem(Guid Id, string EmployeeNumber, string DisplayName, string? Department, DateOnly? TerminationDate, IReadOnlyList<AccountItem> ActiveAccounts);

public record IdentityEventItem(Guid Id, IdentityEventKind Kind, DateTimeOffset OccurredAt, string Source, Guid IdentityId, string IdentityName,
    string EmployeeNumber, string? Department, string? Details);

// ---------- Rollar va SoD (E bosqichi) ----------
public record EntitlementPick(Guid Id, string ApplicationCode, string Name, bool IsPrivileged, RiskLevel Risk);

public record RoleListItem(Guid Id, string Code, string Name, string? Description, RiskLevel Risk, bool IsRequestable,
    int EntitlementCount, int MemberCount, int RuleCount);

public record RoleRuleItem(Guid Id, string? Department, string? JobTitle, int MatchCount, DateTimeOffset CreatedAt, string CreatedBy);

public record RoleMemberItem(Guid IdentityId, string Name, string EmployeeNumber, string? Department, IdentityStatus Status,
    RoleAssignmentSource Source, DateTimeOffset ValidFrom);

public record RoleDetails(Guid Id, string Code, string Name, string? Description, RiskLevel Risk, bool IsRequestable, DateTimeOffset CreatedAt,
    IReadOnlyList<EntitlementPick> Entitlements, IReadOnlyList<RoleRuleItem> Rules, IReadOnlyList<RoleMemberItem> Members,
    IReadOnlyList<string> SodConflicts);

public record SaveRoleRequest(string Code, string Name, string? Description, RiskLevel Risk, bool IsRequestable);

public record SetRoleEntitlementsRequest(IReadOnlyList<Guid> EntitlementIds);

public record AddRoleRuleRequest(string? Department, string? JobTitle);

public record AttributeValues(IReadOnlyList<string> Departments, IReadOnlyList<string> JobTitles);

public record IdentityRoleItem(Guid RoleId, string Code, string Name, RoleAssignmentSource Source, DateTimeOffset ValidFrom);

public record IdentityAccess(IReadOnlyList<IdentityRoleItem> Roles, IReadOnlyList<Bank.Idm.Infrastructure.Access.AccessLine> Lines,
    IReadOnlyList<SodViolationItem> Sod);

public record SodRuleItem(Guid Id, string Code, string Name, string? Description, RiskLevel Risk, IReadOnlyList<EntitlementPick> Left,
    IReadOnlyList<EntitlementPick> Right, int ViolationCount, DateTimeOffset CreatedAt, string CreatedBy);

public record SaveSodRuleRequest(string Code, string Name, string? Description, RiskLevel Risk, IReadOnlyList<Guid> Left, IReadOnlyList<Guid> Right);

public record SodViolationItem(Guid RuleId, string RuleCode, string RuleName, RiskLevel Risk, Guid IdentityId, string IdentityName,
    string EmployeeNumber, string? Department, IReadOnlyList<string> Left, IReadOnlyList<string> Right);

public record DriftItem(Guid IdentityId, string Name, string EmployeeNumber, string? Department, IdentityStatus Status, int Missing, int Excess);
