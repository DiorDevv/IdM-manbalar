using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Connectors;
using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Provisioning;

public record ExecuteResult(int Succeeded, int Retrying, int Failed, int Skipped);

/// <summary>
/// Navbatdagi amallarni tizimda bajaradi. Har bir amal avval qulf ostida "bajarilmoqda" deb olinadi (ikki ishlovchi bir amalni
/// bajarmaydi), keyin tizimga yoziladi va IdM bazasi darhol yangilanadi (keyingi sinxronlash tasdiqlaydi).
/// Vaqtinchalik xato (tarmoq, server band) — kechikish bilan qayta urinish va to'plam to'xtatiladi (qolgan amallar urinishlarini
/// behuda sarflamasligi uchun); doimiy xato yoki urinishlar tugashi — "xato", administrator ko'rib chiqadi.
/// Tartib: avval bloklash va a'zolikdan chiqarish (xavfsizlik), keyin yaratish va qo'shish.
/// </summary>
public class ProvisioningExecutor(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    public const string SystemActor = "system";
    public const string SystemActorName = "IdM provisioning";
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];

    public async Task<ExecuteResult> ExecuteDueAsync(Guid applicationId, IProvisioningConnector connector, ProvisioningSettings settings,
        int batchSize = 100, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var due = await db.ProvisioningOperations.AsNoTracking()
            .Where(o => o.ApplicationId == applicationId && o.Status == ProvisioningStatus.Queued && (o.NextAttemptAt == null || o.NextAttemptAt <= now))
            .OrderBy(o => o.Kind == ProvisioningKind.DisableAccount ? 0 : o.Kind == ProvisioningKind.RemoveMembership ? 1 : o.Kind == ProvisioningKind.CreateAccount ? 2 : 3)
            .ThenBy(o => o.CreatedAt).Select(o => o.Id).Take(batchSize).ToListAsync(ct);
        int succeeded = 0, retrying = 0, failed = 0, skipped = 0;
        foreach (var id in due)
        {
            ct.ThrowIfCancellationRequested();
            var op = await ClaimAsync(applicationId, id, ct);
            if (op is null) { skipped++; continue; }
            var outcome = await RunAsync(op, connector, settings, ct);
            switch (outcome)
            {
                case ProvisioningStatus.Succeeded or ProvisioningStatus.Obsolete: succeeded++; break;
                case ProvisioningStatus.Queued: retrying++; break;
                default: failed++; break;
            }
            // Tizim javob bermayapti: qolganlari keyingi aylanishda (har biri urinishini behuda sarflamasin)
            if (outcome == ProvisioningStatus.Queued) break;
        }
        return new ExecuteResult(succeeded, retrying, failed, skipped);
    }

    /// <summary>Navbatdagi amalni qulf ostida "bajarilmoqda" holatiga o'tkazadi; boshqa ishlovchi olgan yoki o'zgargan bo'lsa — null.</summary>
    private async Task<ProvisioningOperation?> ClaimAsync(Guid applicationId, Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ProvisioningLocks.AcquireAsync(db, applicationId, ct);
        var op = await db.ProvisioningOperations.Include(o => o.Application).Include(o => o.Account).Include(o => o.Entitlement).Include(o => o.Identity)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (op is not { Status: ProvisioningStatus.Queued }) return null;
        op.Status = ProvisioningStatus.Running;
        op.Attempts++;
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return op;
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
    }

    private async Task<ProvisioningStatus> RunAsync(ProvisioningOperation op, IProvisioningConnector connector, ProvisioningSettings settings, CancellationToken ct)
    {
        string? error;
        bool transient;
        try
        {
            var (status, changed, account) = await ApplyAsync(op, connector, settings, ct);
            op.Status = status;
            op.CompletedAt = time.GetUtcNow();
            op.NextAttemptAt = null;
            op.LastError = null;
            var entry = status == ProvisioningStatus.Obsolete
                ? Entry(op, "provisioning.obsolete", account, new { reason = "no-longer-needed" })
                : Entry(op, Action(op.Kind), account, new { changed });
            await audit.SaveChangesAsync(entry, CancellationToken.None);
            return status;
        }
        catch (ConnectorException e)
        {
            (error, transient) = (e.Message, e.IsTransient);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Server to'xtatilmoqda: amal navbatga qaytadi, urinish hisoblanmaydi
            (error, transient) = ("Server to'xtatilgani uchun to'xtatildi", true);
            op.Attempts--;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            (error, transient) = ($"{e.GetType().Name}: {e.Message}", false);
        }

        // Muvaffaqiyatsiz: IdM'dagi saqlanmagan o'zgarishlar tashlanadi, amal qayta o'qilib faqat uning holati yoziladi
        var now = time.GetUtcNow();
        var attempts = op.Attempts;
        db.ChangeTracker.Clear();
        op = await db.ProvisioningOperations.Include(o => o.Application).Include(o => o.Account).Include(o => o.Entitlement).Include(o => o.Identity)
            .SingleAsync(o => o.Id == op.Id, CancellationToken.None);
        op.Attempts = attempts;
        op.LastError = error.Length <= 2000 ? error : error[..1997] + "...";
        if (transient && op.Attempts < settings.MaxAttempts)
        {
            op.Status = ProvisioningStatus.Queued;
            op.NextAttemptAt = now + Backoff[Math.Clamp(op.Attempts - 1, 0, Backoff.Length - 1)];
            await db.SaveChangesAsync(CancellationToken.None);
            return ProvisioningStatus.Queued;
        }
        op.Status = ProvisioningStatus.Failed;
        op.NextAttemptAt = null;
        op.CompletedAt = now;
        await audit.SaveChangesAsync(Entry(op, "provisioning.failed", op.Account, new { error = op.LastError, op.Attempts }), CancellationToken.None);
        return ProvisioningStatus.Failed;
    }

    private async Task<(ProvisioningStatus Status, bool Changed, Account? Account)> ApplyAsync(ProvisioningOperation op, IProvisioningConnector connector,
        ProvisioningSettings settings, CancellationToken ct)
    {
        if (op.Kind == ProvisioningKind.CreateAccount) return await CreateAsync(op, connector, settings, ct);

        var account = op.Account;
        if (account is null || account.Status == AccountStatus.Deleted)
            throw new ConnectorException("Akkaunt IdM'da endi yo'q (tizimdan o'chirilgan)");
        bool changed;
        switch (op.Kind)
        {
            case ProvisioningKind.DisableAccount:
                changed = await connector.DisableAccountAsync(account.ExternalId, ct);
                account.Status = AccountStatus.Disabled;
                break;
            case ProvisioningKind.AddMembership:
            case ProvisioningKind.RemoveMembership:
                var entitlement = op.Entitlement;
                if (entitlement is null || entitlement.DeletedAt is not null) throw new ConnectorException("Guruh tizimda endi yo'q");
                var add = op.Kind == ProvisioningKind.AddMembership;
                changed = add ? await connector.AddMemberAsync(account.ExternalId, entitlement.ExternalId, ct)
                    : await connector.RemoveMemberAsync(account.ExternalId, entitlement.ExternalId, ct);
                var link = await db.AccountEntitlements.FirstOrDefaultAsync(x => x.AccountId == account.Id && x.EntitlementId == entitlement.Id, CancellationToken.None);
                if (add && link is null) db.AccountEntitlements.Add(new AccountEntitlement { AccountId = account.Id, EntitlementId = entitlement.Id, GrantedAt = time.GetUtcNow() });
                if (!add && link is not null) db.AccountEntitlements.Remove(link);
                break;
            default:
                throw new InvalidOperationException($"Noma'lum amal: {op.Kind}");
        }
        return (ProvisioningStatus.Succeeded, changed, account);
    }

    /// <summary>
    /// Akkaunt yaratish. Oldingi urinishda yaratilgan bo'lishi mumkin (javob yo'qolgan): avval tabel raqami bo'yicha qidiriladi.
    /// Login IdM'da ham, tizimda ham band bo'lmagan birinchi nomzod.
    /// </summary>
    private async Task<(ProvisioningStatus, bool, Account?)> CreateAsync(ProvisioningOperation op, IProvisioningConnector connector, ProvisioningSettings settings,
        CancellationToken ct)
    {
        var identity = op.Identity ?? throw new ConnectorException("Xodim topilmadi");
        if (identity.Status is not (IdentityStatus.Active or IdentityStatus.Pending)) return (ProvisioningStatus.Obsolete, false, null);
        if (await db.Accounts.AnyAsync(a => a.ApplicationId == op.ApplicationId && a.IdentityId == identity.Id && a.Status != AccountStatus.Deleted, ct))
            return (ProvisioningStatus.Obsolete, false, null);

        var created = false;
        var external = await connector.FindByEmployeeNumberAsync(identity.EmployeeNumber, ct);
        if (external is null)
        {
            string? login = null;
            foreach (var candidate in LoginGenerator.Candidates(identity.FirstName, identity.LastName, settings.LoginMaxLength))
            {
                if (await db.Accounts.AnyAsync(a => a.ApplicationId == op.ApplicationId && a.Login.ToLower() == candidate, ct)) continue;
                if (await connector.FindByLoginAsync(candidate, ct) is not null) continue;
                login = candidate;
                break;
            }
            if (login is null) throw new ConnectorException($"{identity.LastName} {identity.FirstName}: bo'sh login topilmadi (ism lotin harflarida emas yoki hammasi band)");
            external = await connector.CreateAccountAsync(new NewAccount(login, identity.FirstName, identity.LastName, $"{identity.LastName} {identity.FirstName}",
                identity.EmployeeNumber, identity.Email, identity.Department, identity.JobTitle), ct);
            created = true;
        }

        // Sinxronlash shu orada topgan bo'lishi mumkin: mavjud yozuv xodimga bog'lanadi
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.ApplicationId == op.ApplicationId && a.ExternalId == external.ExternalId, CancellationToken.None);
        if (account is null)
        {
            account = new Account
            {
                ApplicationId = op.ApplicationId, ExternalId = external.ExternalId, Login = external.Login, CreatedAt = time.GetUtcNow(),
                Status = external.IsEnabled ? AccountStatus.Enabled : AccountStatus.Disabled
            };
            db.Accounts.Add(account);
        }
        (account.IdentityId, account.EmployeeNumber, account.DistinguishedName, account.Email) =
            (identity.Id, external.EmployeeNumber ?? identity.EmployeeNumber, external.DistinguishedName, external.Email);
        account.DisplayName = external.DisplayName ?? $"{identity.LastName} {identity.FirstName}";
        op.AccountId = account.Id;
        op.Summary = external.Login;
        return (ProvisioningStatus.Succeeded, created, account);
    }

    private static string Action(ProvisioningKind kind) => kind switch
    {
        ProvisioningKind.CreateAccount => "provisioning.account-created",
        ProvisioningKind.DisableAccount => "provisioning.account-disabled",
        ProvisioningKind.AddMembership => "provisioning.membership-added",
        _ => "provisioning.membership-removed"
    };

    /// <summary>Audit obyekti — akkaunt (xodim tarixida ko'rinadi); akkaunt bo'lmasa — xodim.</summary>
    private static AuditEntry Entry(ProvisioningOperation op, string action, Account? account, object extra)
    {
        var details = JsonSerializer.Serialize(new
        {
            operationId = op.Id, application = op.Application.Code, kind = op.Kind.ToString(), op.Reason, login = account?.Login ?? op.Summary,
            group = op.Entitlement?.Name, employeeNumber = op.Identity?.EmployeeNumber, approvedBy = op.ApprovedBy, extra
        }, Json);
        return account is not null
            ? new AuditEntry(SystemActor, SystemActorName, action, "account", account.Id.ToString(), details)
            : new AuditEntry(SystemActor, SystemActorName, action, "identity", op.IdentityId?.ToString(), details);
    }
}
