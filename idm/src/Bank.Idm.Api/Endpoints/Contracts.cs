using Bank.Idm.Domain;

namespace Bank.Idm.Api.Endpoints;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);

public record MeResponse(string Login, string? Name, IReadOnlyList<string> Roles, Guid? IdentityId, bool HasTeam = false, int InboxCount = 0, int ReviewCount = 0,
    IReadOnlyList<string>? MfaRequiredFor = null, string? MfaStepUpAcr = null, int OwnedRoles = 0, int OwnedRolesDue = 0, int MoverCount = 0);

public record CertifyRoleBody(IReadOnlyList<Guid> Keep, string? Comment);

/// <param name="Due">Tasdiqlash muddati kelgan (hech qachon tasdiqlanmagan yoki RoleCertificationDays o'tgan).</param>
public record OwnedRoleItem(Guid Id, string Code, string Name, string? Description, RiskLevel Risk, string? OwnerName, int Members,
    IReadOnlyList<EntitlementPick> Entitlements, IReadOnlyList<string> Rules, DateTimeOffset? CertifiedAt, string? CertifiedBy, bool Due);

public record DashboardSummary(
    int Identities, int ActiveIdentities, int SuspendedIdentities, int TerminatedIdentities,
    int Applications, int Accounts, int DisabledAccounts, int OrphanAccounts, int LeaversWithActiveAccounts, int SodViolations, int PendingRequests,
    DateTimeOffset? LastAuditAt, IReadOnlyList<ConnectorStatus> Connectors, DateTimeOffset? CalculatedAt = null);

/// <summary>Bosh sahifa hisoblari (keshlanadi, <see cref="SharedSnapshot{T}"/>).</summary>
public record DashboardCounts(
    int Identities, int ActiveIdentities, int SuspendedIdentities, int TerminatedIdentities,
    int Applications, int Accounts, int DisabledAccounts, int OrphanAccounts, int LeaversWithActiveAccounts, int SodViolations, int PendingRequests,
    DateTimeOffset? LastAuditAt);

/// <summary>Tizim konnektorining holati: oxirgi ishga tushish va oxirgi muvaffaqiyatli sinxronlash.</summary>
public record ConnectorStatus(string Code, string Name, ApplicationKind Kind, bool IsRunning, ConnectorRunItem? LastRun, DateTimeOffset? LastSuccessAt);

public record IdentityListItem(Guid Id, string EmployeeNumber, string DisplayName, string? Email, string? Department,
    string? JobTitle, IdentityStatus Status, IdentitySource Source, int AccountCount, DateTimeOffset UpdatedAt, string? Branch = null);

public record IdentityDetails(Guid Id, string EmployeeNumber, string FirstName, string LastName, string? MiddleName,
    string? Email, string? Department, string? JobTitle, Guid? ManagerId, string? ManagerName, IdentityStatus Status,
    IdentitySource Source, DateOnly? HireDate, DateOnly? TerminationDate, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<AccountItem> Accounts, string? Branch = null);

public record AccountItem(Guid Id, string ApplicationCode, string ApplicationName, string Login, AccountStatus Status, AccountKind Kind,
    DateTimeOffset? LastSyncedAt, IReadOnlyList<EntitlementRef> Entitlements);

/// <param name="Via">Meros a'zolik: qaysi bevosita guruh orqali (bevosita bo'lsa — null).</param>
public record EntitlementRef(Guid Id, string Name, bool IsPrivileged, string? Via = null);

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

public record RoleRuleItem(Guid Id, string? Department, string? JobTitle, int MatchCount, DateTimeOffset CreatedAt, string CreatedBy, string? Branch = null);

public record RoleMemberItem(Guid IdentityId, string Name, string EmployeeNumber, string? Department, IdentityStatus Status,
    RoleAssignmentSource Source, DateTimeOffset ValidFrom);

public record RoleDetails(Guid Id, string Code, string Name, string? Description, RiskLevel Risk, bool IsRequestable, DateTimeOffset CreatedAt,
    IReadOnlyList<EntitlementPick> Entitlements, IReadOnlyList<RoleRuleItem> Rules, IReadOnlyList<RoleMemberItem> Members,
    IReadOnlyList<string> SodConflicts, Guid? OwnerId = null, string? OwnerName = null, IdentityStatus? OwnerStatus = null,
    DateTimeOffset? CertifiedAt = null, string? CertifiedBy = null);

/// <param name="OwnerId">Rol egasi: arizalarda rahbardan keyingi tasdiqlovchi (bo'sh — IdM administratorlari).</param>
public record SaveRoleRequest(string Code, string Name, string? Description, RiskLevel Risk, bool IsRequestable, Guid? OwnerId = null);

public record SetRoleEntitlementsRequest(IReadOnlyList<Guid> EntitlementIds);

public record AddRoleRuleRequest(string? Department, string? JobTitle, string? Branch = null);

public record AttributeValues(IReadOnlyList<string> Departments, IReadOnlyList<string> JobTitles, IReadOnlyList<string>? Branches = null);

/// <param name="MoverDueAt">Xodim boshqa filial/bo'limga o'tgan: shu vaqtgacha yangi rahbar qoldirmasa, rol tugaydi.</param>
public record IdentityRoleItem(Guid RoleId, string Code, string Name, RoleAssignmentSource Source, DateTimeOffset ValidFrom,
    Guid AssignmentId = default, DateTimeOffset? ValidTo = null, DateTimeOffset? MoverDueAt = null);

/// <summary>O'tgan xodimning qaror kutilayotgan roli.</summary>
public record MoverItem(Guid AssignmentId, Guid IdentityId, string Name, string EmployeeNumber, string? Branch, string? Department, string? JobTitle,
    Guid RoleId, string RoleCode, string RoleName, RiskLevel Risk, RoleAssignmentSource Source, DateTimeOffset? ValidTo, DateTimeOffset DueAt,
    string? ManagerName, bool CanDecide);

public record MoverDecisionBody(string Decision, string? Comment);

public record IdentityAccess(IReadOnlyList<IdentityRoleItem> Roles, IReadOnlyList<Bank.Idm.Infrastructure.Access.AccessLine> Lines,
    IReadOnlyList<SodViolationItem> Sod);

public record SodRuleItem(Guid Id, string Code, string Name, string? Description, RiskLevel Risk, IReadOnlyList<EntitlementPick> Left,
    IReadOnlyList<EntitlementPick> Right, int ViolationCount, DateTimeOffset CreatedAt, string CreatedBy);

public record SaveSodRuleRequest(string Code, string Name, string? Description, RiskLevel Risk, IReadOnlyList<Guid> Left, IReadOnlyList<Guid> Right);

public record SodViolationItem(Guid RuleId, string RuleCode, string RuleName, RiskLevel Risk, Guid IdentityId, string IdentityName,
    string EmployeeNumber, string? Department, IReadOnlyList<string> Left, IReadOnlyList<string> Right,
    Bank.Idm.Infrastructure.Access.SodExceptionInfo? Exception = null);

public record SodExceptionBody(Guid RuleId, Guid IdentityId, string Reason, string CompensatingControl, DateOnly ValidUntil);
public record SodExceptionRevokeBody(string? Comment);

/// <param name="State">Active, Expired, Revoked.</param>
public record SodExceptionItem(Guid Id, Guid RuleId, string RuleCode, string RuleName, Guid IdentityId, string IdentityName, string EmployeeNumber,
    string Reason, string CompensatingControl, DateTimeOffset ValidFrom, DateTimeOffset ValidTo, string State, DateTimeOffset CreatedAt, string CreatedBy,
    DateTimeOffset? RevokedAt, string? RevokedBy);

public record DriftItem(Guid IdentityId, string Name, string EmployeeNumber, string? Department, IdentityStatus Status, int Missing, int Excess);

// ---------- Arizalar (F bosqichi) ----------
public record CatalogRole(Guid Id, string Code, string Name, string? Description, RiskLevel Risk, IReadOnlyList<string> Entitlements);

public record PersonBrief(Guid Id, string Name, string EmployeeNumber, string? Department, string? JobTitle);

public record DelegationBody(Guid? DelegatorId, Guid DelegateId, DateOnly From, DateOnly Until, string? Reason);

/// <param name="Active">Hozir amalda (boshlangan va tugamagan).</param>
public record DelegationItem(Guid Id, Guid DelegatorId, string DelegatorName, Guid DelegateId, string DelegateName, DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo, string? Reason, string CreatedBy, bool Active);

public record MyDelegations(IReadOnlyList<DelegationItem> Given, IReadOnlyList<DelegationItem> Received);

public record PreviewRequestBody(Guid? BeneficiaryId, Guid RoleId);

public record PreviewStep(ApprovalStepKind Kind, string? ApproverName, bool Auto);

public record RequestPreview(bool CanSubmit, string? Blocker, IReadOnlyList<PreviewStep> Steps, IReadOnlyList<Bank.Idm.Infrastructure.Requests.SodConflictInfo> Sod);

public record CreateRequestBody(Guid? BeneficiaryId, Guid RoleId, string? Justification, DateTimeOffset? ValidTo);

public record DecisionBody(bool Approve, string? Comment);

public record RequestListItem(Guid Id, AccessRequestStatus Status, Guid RoleId, string RoleCode, string RoleName, RiskLevel Risk,
    Guid BeneficiaryId, string BeneficiaryName, string BeneficiaryNumber, string RequesterName, DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt,
    ApprovalStepKind? CurrentStep, string? CurrentApprover, bool HasSod);

public record ApprovalItem(int Step, ApprovalStepKind Kind, Guid? ApproverId, string? ApproverName, ApprovalDecision Decision, string? Comment,
    DateTimeOffset? DecidedAt, string? DecidedBy, string? DecidedByName);

public record RequestDetails(Guid Id, AccessRequestStatus Status, Guid RoleId, string RoleCode, string RoleName, string? RoleDescription, RiskLevel Risk,
    IReadOnlyList<string> Entitlements, PersonBrief Beneficiary, PersonBrief Requester, string Justification, DateTimeOffset? ValidTo,
    DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt, IReadOnlyList<ApprovalItem> Approvals,
    IReadOnlyList<Bank.Idm.Infrastructure.Requests.SodConflictInfo> SodConflicts, bool CanDecide, bool CanCancel);

public record ProvisioningOperationItem(Guid Id, string ApplicationCode, ProvisioningKind Kind, ProvisioningStatus Status, string Reason,
    Guid? IdentityId, string? IdentityName, string? EmployeeNumber, Guid? AccountId, string? Login, string? Group, bool GroupPrivileged, string? Summary,
    DateTimeOffset CreatedAt, string? ApprovedBy, string? ApprovedByName, DateTimeOffset? ApprovedAt, int Attempts, DateTimeOffset? NextAttemptAt,
    string? LastError, DateTimeOffset? CompletedAt);

/// <summary>Tizim bo'yicha yozish holati: rejim va amallar soni.</summary>
public record ProvisioningApplicationStatus(string Code, string Name, Bank.Idm.Infrastructure.Sync.ProvisioningMode Mode, bool CreateAccounts,
    int MaxAutoOperations, int Proposed, int Queued, int Running, int Failed, int SucceededLast24h,
    bool EnableReturning = false, bool EnableNewAccounts = false);

public record ProvisioningActionBody(IReadOnlyList<Guid> Ids);

public record CampaignBody(string? Name, string? Description, string? Department, Guid? ApplicationId, bool PrivilegedOnly, DateOnly DueDate,
    bool RevokeUnreviewed = false);

public record CampaignItem(Guid Id, string Name, string? Description, AccessReviewStatus Status, string? Department, string? ApplicationCode, bool PrivilegedOnly,
    DateOnly DueDate, DateTimeOffset CreatedAt, string CreatedBy, string? CreatedByName, DateTimeOffset? ClosedAt, int Total, int Kept, int Revoked, int Pending,
    int RevokedOpen, bool RevokeUnreviewed = false);

public record CampaignReviewerProgress(Guid? ReviewerId, string? ReviewerName, int Total, int Pending);

public record CampaignDetails(CampaignItem Campaign, IReadOnlyList<CampaignReviewerProgress> Reviewers);

/// <summary>Attestatsiya navbati sahifasi: Skip/Take — xodimlar soni bo'yicha; Items — shu xodimlarning barcha yozuvlari.</summary>
public record ReviewInbox(IReadOnlyList<ReviewItemDto> Items, int TotalItems, int TotalPeople, int Skip, int Take);

public record ReviewItemDto(Guid Id, Guid CampaignId, string CampaignName, DateOnly DueDate, AccessReviewItemKind Kind, Guid IdentityId, string IdentityName,
    string EmployeeNumber, string? Department, string? JobTitle, IdentityStatus IdentityStatus, string ApplicationCode, string Title, string? Subtitle,
    bool IsPrivileged, Guid? ReviewerId, string? ReviewerName, AccessReviewDecision Decision, string? Comment, DateTimeOffset? DecidedAt, string? DecidedBy,
    string? DecidedByName, DateTimeOffset? RemediatedAt);

public record ReviewDecisionBody(IReadOnlyList<Guid> Ids, bool Keep, string? Comment);

public record CloseCampaignBody(bool Cancel);
