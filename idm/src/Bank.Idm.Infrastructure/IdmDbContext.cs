using System.Text;
using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure;

public class IdmDbContext(DbContextOptions<IdmDbContext> options) : DbContext(options)
{
    public DbSet<Identity> Identities => Set<Identity>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();
    public DbSet<AccountEntitlement> AccountEntitlements => Set<AccountEntitlement>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleEntitlement> RoleEntitlements => Set<RoleEntitlement>();
    public DbSet<IdentityRole> IdentityRoles => Set<IdentityRole>();
    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<PortalRoleAssignment> PortalRoleAssignments => Set<PortalRoleAssignment>();
    public DbSet<ConnectorRun> ConnectorRuns => Set<ConnectorRun>();
    public DbSet<IdentityEvent> IdentityEvents => Set<IdentityEvent>();
    public DbSet<RoleRule> RoleRules => Set<RoleRule>();
    public DbSet<SodRule> SodRules => Set<SodRule>();
    public DbSet<SodRuleEntitlement> SodRuleEntitlements => Set<SodRuleEntitlement>();
    public DbSet<ProvisioningOperation> ProvisioningOperations => Set<ProvisioningOperation>();
    public DbSet<AccessReviewCampaign> AccessReviewCampaigns => Set<AccessReviewCampaign>();
    public DbSet<AccessReviewItem> AccessReviewItems => Set<AccessReviewItem>();
    public DbSet<ClusterNode> ClusterNodes => Set<ClusterNode>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Identity>(e =>
        {
            e.HasIndex(x => x.EmployeeNumber).IsUnique();
            e.HasIndex(x => x.Email);
            e.Property(x => x.EmployeeNumber).HasMaxLength(64);
            e.Property(x => x.FirstName).HasMaxLength(128);
            e.Property(x => x.LastName).HasMaxLength(128);
            e.Property(x => x.MiddleName).HasMaxLength(128);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Department).HasMaxLength(256);
            e.Property(x => x.JobTitle).HasMaxLength(256);
            e.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.SetNull);
            e.Ignore(x => x.DisplayName);
        });
        b.Entity<Application>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(256);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<Account>(e =>
        {
            e.HasIndex(x => new { x.ApplicationId, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.Login });
            e.HasIndex(x => x.IdentityId);
            e.HasIndex(x => x.EmployeeNumber);
            e.Property(x => x.ExternalId).HasMaxLength(256);
            e.Property(x => x.Login).HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.EmployeeNumber).HasMaxLength(64);
            e.Property(x => x.DistinguishedName).HasMaxLength(1024);
            e.HasOne(x => x.Application).WithMany(x => x.Accounts).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Identity).WithMany(x => x.Accounts).HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<Entitlement>(e =>
        {
            e.HasIndex(x => new { x.ApplicationId, x.ExternalId }).IsUnique();
            e.Property(x => x.ExternalId).HasMaxLength(512);
            e.Property(x => x.Name).HasMaxLength(256);
            e.HasOne(x => x.Application).WithMany(x => x.Entitlements).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccountEntitlement>(e =>
        {
            e.HasKey(x => new { x.AccountId, x.EntitlementId });
            e.HasOne(x => x.Account).WithMany(x => x.Entitlements).HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Entitlement).WithMany().HasForeignKey(x => x.EntitlementId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Role>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<RoleEntitlement>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.EntitlementId });
            e.HasOne(x => x.Role).WithMany(x => x.Entitlements).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Entitlement).WithMany().HasForeignKey(x => x.EntitlementId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RoleRule>(e =>
        {
            e.HasIndex(x => x.RoleId);
            e.Property(x => x.Department).HasMaxLength(256);
            e.Property(x => x.JobTitle).HasMaxLength(256);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            e.HasOne(x => x.Role).WithMany(x => x.Rules).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SodRule>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
        });
        b.Entity<SodRuleEntitlement>(e =>
        {
            e.HasKey(x => new { x.SodRuleId, x.EntitlementId });
            e.HasOne(x => x.SodRule).WithMany(x => x.Entitlements).HasForeignKey(x => x.SodRuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Entitlement).WithMany().HasForeignKey(x => x.EntitlementId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<IdentityRole>(e =>
        {
            // Bir xodimda bir rol bir manbadan faqat bitta amaldagi yozuv (parallel qayta hisoblashdan himoya)
            e.HasIndex(x => new { x.IdentityId, x.RoleId, x.Source }).IsUnique().HasFilter("valid_to IS NULL");
            e.HasOne(x => x.Identity).WithMany(x => x.Roles).HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccessRequest>(e =>
        {
            e.HasIndex(x => x.Status);
            e.Property(x => x.Justification).HasMaxLength(2000);
            e.Property(x => x.SodConflicts).HasColumnType("text");
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.BeneficiaryId, x.Status });
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Beneficiary).WithMany().HasForeignKey(x => x.BeneficiaryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Approval>(e =>
        {
            e.HasIndex(x => new { x.ApproverId, x.Decision });
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.Property(x => x.DecidedBy).HasMaxLength(256);
            e.Property(x => x.DecidedByName).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne(x => x.AccessRequest).WithMany(x => x.Approvals).HasForeignKey(x => x.AccessRequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Approver).WithMany().HasForeignKey(x => x.ApproverId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ProvisioningOperation>(e =>
        {
            // Bir amal bir vaqtda faqat bitta faol yozuvda (parallel rejalashtirishdan himoya)
            e.HasIndex(x => x.DedupKey).IsUnique().HasFilter("status IN ('Proposed', 'Queued', 'Running', 'Failed')");
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.ApplicationId, x.CreatedAt });
            e.HasIndex(x => new { x.AccountId, x.EntitlementId });
            e.HasIndex(x => x.IdentityId);
            e.Property(x => x.Reason).HasMaxLength(32);
            e.Property(x => x.Summary).HasMaxLength(1000);
            e.Property(x => x.DedupKey).HasMaxLength(256);
            e.Property(x => x.ApprovedBy).HasMaxLength(256);
            e.Property(x => x.ApprovedByName).HasMaxLength(256);
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Identity).WithMany().HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Entitlement).WithMany().HasForeignKey(x => x.EntitlementId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<AccessReviewCampaign>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Department).HasMaxLength(256);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            e.Property(x => x.CreatedByName).HasMaxLength(256);
            e.Property(x => x.ClosedBy).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccessReviewItem>(e =>
        {
            e.HasIndex(x => new { x.CampaignId, x.Decision });
            e.HasIndex(x => new { x.ReviewerId, x.Decision });
            e.HasIndex(x => new { x.Decision, x.RemediatedAt });
            e.HasIndex(x => x.IdentityId);
            e.Property(x => x.ApplicationCode).HasMaxLength(128);
            e.Property(x => x.Title).HasMaxLength(256);
            e.Property(x => x.Subtitle).HasMaxLength(256);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.Property(x => x.DecidedBy).HasMaxLength(256);
            e.Property(x => x.DecidedByName).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne(x => x.Campaign).WithMany(x => x.Items).HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Identity).WithMany().HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.IdentityRole).WithMany().HasForeignKey(x => x.IdentityRoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Entitlement).WithMany().HasForeignKey(x => x.EntitlementId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.TargetType, x.TargetId });
            e.HasIndex(x => x.ActorId);
            e.Property(x => x.ActorId).HasMaxLength(256);
            e.Property(x => x.ActorName).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(128);
            e.Property(x => x.TargetType).HasMaxLength(64);
            e.Property(x => x.TargetId).HasMaxLength(256);
            // text, jsonb emas: jsonb JSON'ni normallashtiradi va saqlangan xesh qayta hisoblanganiga mos kelmay qoladi
            e.Property(x => x.Details).HasColumnType("text");
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.CorrelationId).HasMaxLength(128);
            e.Property(x => x.PreviousHash).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.Hash).HasMaxLength(64).IsFixedLength();
            e.HasIndex(x => x.Hash).IsUnique();
        });
        b.Entity<PortalRoleAssignment>(e =>
        {
            e.HasIndex(x => new { x.Login, x.Role }).IsUnique();
            e.Property(x => x.Login).HasMaxLength(256);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
        });
        b.Entity<ClusterNode>(e =>
        {
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.Host).HasMaxLength(256);
        });
        b.Entity<ConnectorRun>(e =>
        {
            e.HasIndex(x => new { x.ApplicationId, x.StartedAt });
            // Bir tizimda bir vaqtda bitta sinxronlash — barcha server nusxalari uchun (baza kafolatlaydi)
            e.HasIndex(x => x.ApplicationId).IsUnique().HasFilter("status = 'Running'").HasDatabaseName("ix_connector_runs_one_running");
            e.Property(x => x.RunnerId).HasMaxLength(128);
            e.Property(x => x.TriggeredBy).HasMaxLength(256);
            e.Property(x => x.Error).HasMaxLength(4000);
            e.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<IdentityEvent>(e =>
        {
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.IdentityId, x.OccurredAt });
            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.Details).HasColumnType("text");
            e.HasOne(x => x.Identity).WithMany().HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Cascade);
        });

        // Enum'lar matn sifatida (DBA va hisobotlar uchun o'qiladigan), jadval va ustunlar snake_case.
        foreach (var entity in b.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));
            foreach (var property in entity.GetProperties())
            {
                // Npgsql row version — PostgreSQL'ning tizim ustuni xmin (jadvalga ustun qo'shilmaydi)
                if (property.IsConcurrencyToken && property.ClrType == typeof(uint)) { property.SetColumnName("xmin"); continue; }
                property.SetColumnName(ToSnakeCase(property.Name));
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (type.IsEnum) property.SetProviderClrType(typeof(string));
                if (type.IsEnum) property.SetMaxLength(32);
            }
            foreach (var key in entity.GetKeys()) key.SetName(ToSnakeCase(key.GetName()!));
            foreach (var fk in entity.GetForeignKeys()) fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));
            foreach (var index in entity.GetIndexes()) index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_' && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])))) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
