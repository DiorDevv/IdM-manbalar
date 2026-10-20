using Bank.Idm.Domain;

namespace Bank.Idm.Api.Endpoints;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);

public record MeResponse(string Login, string? Name, IReadOnlyList<string> Roles, Guid? IdentityId);

public record DashboardSummary(
    int Identities, int ActiveIdentities, int SuspendedIdentities, int TerminatedIdentities,
    int Applications, int Accounts, int OrphanAccounts, int PendingRequests, DateTimeOffset? LastAuditAt);

public record IdentityListItem(Guid Id, string EmployeeNumber, string DisplayName, string? Email, string? Department,
    string? JobTitle, IdentityStatus Status, IdentitySource Source, int AccountCount, DateTimeOffset UpdatedAt);

public record IdentityDetails(Guid Id, string EmployeeNumber, string FirstName, string LastName, string? MiddleName,
    string? Email, string? Department, string? JobTitle, Guid? ManagerId, string? ManagerName, IdentityStatus Status,
    IdentitySource Source, DateOnly? HireDate, DateOnly? TerminationDate, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<AccountItem> Accounts);

public record AccountItem(Guid Id, string ApplicationCode, string ApplicationName, string Login, AccountStatus Status, AccountKind Kind, DateTimeOffset? LastSyncedAt);

public record CreateIdentityRequest(string EmployeeNumber, string FirstName, string LastName, string? MiddleName,
    string? Email, string? Department, string? JobTitle, DateOnly? HireDate);

public record ApplicationItem(Guid Id, string Code, string Name, string? Description, ApplicationKind Kind, RiskLevel Risk,
    bool IsActive, int AccountCount, int EntitlementCount);

public record AuditItem(long Id, DateTimeOffset OccurredAt, string ActorId, string? ActorName, string Action,
    string? TargetType, string? TargetId, string? Details, string? IpAddress, string? CorrelationId);
