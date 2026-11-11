using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Api.Sync;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Security;

/// <summary>Audit yozuvidagi amal bajaruvchi: joriy foydalanuvchi yoki (ikki kishilik tasdiqda) so'rovni yuborgan administrator.</summary>
public interface IAuditActor
{
    string Login { get; }
    string? Name { get; }
    string? IpAddress { get; }
    string? CorrelationId { get; }
}

public record AuditActor(string Login, string? Name, string? IpAddress, string? CorrelationId) : IAuditActor;

/// <summary>Ikki kishilik tasdiq ostidagi amallar.</summary>
public static class ChangeKinds
{
    public const string PortalRoleGrant = "portal-role.grant";
    public const string PortalRoleRevoke = "portal-role.revoke";
    public const string RoleEntitlements = "role.entitlements";
    public const string RoleRuleAdd = "role.rule-add";
    public const string RoleRuleRemove = "role.rule-remove";
    public const string RoleDelete = "role.delete";
    public const string SodRuleCreate = "sod.rule-create";
    public const string SodRuleDelete = "sod.rule-delete";
    public const string SodExceptionGrant = "sod.exception-grant";
    public const string ConnectorMassDelete = "connector.mass-delete";
}

public record PortalRoleChange(string Login, PortalRole Role);
public record RoleEntitlementsChange(Guid RoleId, IReadOnlyList<Guid> EntitlementIds);
public record RoleRuleAddChange(Guid RoleId, string? Branch, string? Department, string? JobTitle);
public record RoleRuleRemoveChange(Guid RoleId, Guid RuleId);
public record RoleDeleteChange(Guid RoleId);
public record SodRuleDeleteChange(Guid RuleId);
public record MassDeleteChange(string Code);

/// <summary>
/// Ikki kishilik tasdiq ("to'rt ko'z"). <see cref="SubmitAsync"/>: o'chiq bo'lsa — amal darhol; yoqilgan bo'lsa — amal xuddi shu kod bilan
/// bekor qilinadigan tranzaksiyada sinab ko'riladi (xato bo'lsa — darhol shu xato qaytadi), muvaffaqiyatli bo'lsa so'rov saqlanadi (202).
/// <see cref="ApproveAsync"/>: boshqa administrator (so'rovchi emas, o'zgarish o'ziga tegishli emas) tasdiqlasa — amal so'rovchi nomidan
/// bajariladi, "change.approved" bilan bitta tranzaksiyada; shu orada sharoit o'zgargan bo'lsa (masalan ruxsat o'chirilgan) — hech narsa
/// o'zgarmaydi, so'rov "Failed". Holat o'tishlari shartli yangilash bilan: ikki administrator bir vaqtda bosgani faqat bir marta qo'llanadi.
/// </summary>
public partial class AdminChanges(IServiceProvider services, IdmDbContext db, AuditLog audit, CurrentUser user, TimeProvider time,
    IOptionsMonitor<IdmOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Converters = { new JsonStringEnumConverter() }
    };
    public const int MaxCommentLength = 1000;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._@-]{0,127}$")]
    private static partial Regex LoginPattern();

    public bool Enabled => options.CurrentValue.FourEyes.Enabled;

    public async Task<IResult> SubmitAsync(string kind, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        if (!Enabled) return await ExecuteAsync(kind, json, user, ct);

        IResult trial;
        var preview = new List<PreviewEntry>();
        if (kind == ChangeKinds.ConnectorMassDelete) trial = await CheckMassDeleteAsync(json, ct);   // tashqi ta'sirli: sinab ko'rilmaydi
        else
        {
            var captured = new List<AuditEntry>();
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            using (audit.Capture(captured))
            {
                trial = await TryExecuteAsync(kind, json, user, ct);
                await transaction.RollbackAsync(ct);
            }
            preview.AddRange(captured.Select(e => new PreviewEntry(e.Action, e.Details)));
            db.ChangeTracker.Clear();
        }
        if (!IsSuccess(trial)) return trial;

        var now = time.GetUtcNow();
        var request = new ChangeRequest
        {
            Kind = kind, Payload = json, Preview = JsonSerializer.Serialize(preview, Json), RequestedBy = user.Login, RequestedByName = user.Name,
            RequestedIp = user.IpAddress, RequestedAt = now, ExpiresAt = now.AddHours(options.CurrentValue.FourEyes.ExpiryHours)
        };
        db.ChangeRequests.Add(request);
        await audit.SaveChangesAsync(new AuditEntry(user.Login, user.Name, "change.requested", "change-request", request.Id.ToString(),
            JsonSerializer.Serialize(new { kind, actions = preview.Select(p => p.Action) }, Json), user.IpAddress, user.CorrelationId), ct);
        return TypedResults.Accepted($"/api/change-requests/{request.Id}", new ChangePending(ToItem(request, now)));
    }

    public async Task<IResult> ApproveAsync(Guid id, string? comment, CancellationToken ct)
    {
        var text = Clean(comment);
        if (text?.Length > MaxCommentLength) return TypedResults.Problem($"Izoh ko'pi bilan {MaxCommentLength} belgi", statusCode: 400);
        var now = time.GetUtcNow();
        var r = await db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return TypedResults.NotFound();
        if (Problem(r, now) is { } problem) return problem;
        if (await TouchesApproverAsync(r, ct)) return TypedResults.Problem("O'zingizga tegishli o'zgarishni tasdiqlay olmaysiz", statusCode: 403);
        // So'rovchi shu orada administratorlikdan chiqarilgan bo'lsa — uning so'rovi bajarilmaydi
        if (!await db.PortalRoleAssignments.AnyAsync(p => p.Login == r.RequestedBy && p.Role == PortalRole.Admin, ct))
            return await FailAsync(r, "So'rovni yuborgan xodim endi administrator emas", ct);

        IResult result;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var claimed = await db.ChangeRequests.Where(x => x.Id == id && x.Status == ChangeRequestStatus.Pending && x.ExpiresAt > now && x.RequestedBy != user.Login)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ChangeRequestStatus.Applied).SetProperty(x => x.DecidedBy, user.Login)
                    .SetProperty(x => x.DecidedByName, user.Name).SetProperty(x => x.DecidedAt, now).SetProperty(x => x.DecisionComment, text), ct);
            if (claimed == 0) return TypedResults.Problem("So'rov allaqachon hal qilingan", statusCode: 409);
            result = await TryExecuteAsync(r.Kind, r.Payload, new AuditActor(r.RequestedBy, r.RequestedByName, r.RequestedIp, user.CorrelationId), ct);
            if (IsSuccess(result))
            {
                await audit.SaveChangesAsync(new AuditEntry(user.Login, user.Name, "change.approved", "change-request", id.ToString(),
                    JsonSerializer.Serialize(new { kind = r.Kind, requestedBy = r.RequestedBy, comment = text }, Json), user.IpAddress, user.CorrelationId), ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            await transaction.RollbackAsync(ct);
        }
        db.ChangeTracker.Clear();
        return await FailAsync(r, Describe(result), ct);
    }

    public async Task<IResult> RejectAsync(Guid id, string? comment, CancellationToken ct)
    {
        var text = Clean(comment);
        if (text is null || text.Length < 5) return TypedResults.Problem("Rad etish sababini yozing (kamida 5 belgi)", statusCode: 400);
        if (text.Length > MaxCommentLength) return TypedResults.Problem($"Izoh ko'pi bilan {MaxCommentLength} belgi", statusCode: 400);
        var r = await db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return TypedResults.NotFound();
        if (Problem(r, time.GetUtcNow()) is { } problem) return problem;
        return await CloseAsync(r, ChangeRequestStatus.Rejected, "change.rejected", text, ct);
    }

    public async Task<IResult> CancelAsync(Guid id, CancellationToken ct)
    {
        var r = await db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return TypedResults.NotFound();
        if (r.RequestedBy != user.Login) return TypedResults.Problem("So'rovni faqat uni yuborgan administrator bekor qiladi", statusCode: 403);
        if (r.Status != ChangeRequestStatus.Pending) return TypedResults.Problem("So'rov allaqachon hal qilingan", statusCode: 409);
        return await CloseAsync(r, ChangeRequestStatus.Cancelled, "change.cancelled", null, ct);
    }

    /// <summary>Tasdiqlash yoki rad etish mumkinmi: kutilmoqda, eskirmagan, so'rovchi o'zi emas.</summary>
    private IResult? Problem(ChangeRequest r, DateTimeOffset now)
    {
        if (r.Status != ChangeRequestStatus.Pending) return TypedResults.Problem("So'rov allaqachon hal qilingan", statusCode: 409);
        if (r.ExpiresAt <= now) return TypedResults.Problem("So'rov muddati o'tgan: kerak bo'lsa, qaytadan yuboring", statusCode: 409);
        if (r.RequestedBy == user.Login) return TypedResults.Problem("O'z so'rovingizni o'zingiz tasdiqlay yoki rad eta olmaysiz: boshqa administrator kerak", statusCode: 403);
        return null;
    }

    private async Task<IResult> CloseAsync(ChangeRequest r, ChangeRequestStatus status, string action, string? comment, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.ChangeRequests.Where(x => x.Id == r.Id && x.Status == ChangeRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status).SetProperty(x => x.DecidedBy, user.Login).SetProperty(x => x.DecidedByName, user.Name)
                .SetProperty(x => x.DecidedAt, now).SetProperty(x => x.DecisionComment, comment), ct);
        if (changed == 0) return TypedResults.Problem("So'rov allaqachon hal qilingan", statusCode: 409);
        await audit.SaveChangesAsync(new AuditEntry(user.Login, user.Name, action, "change-request", r.Id.ToString(),
            JsonSerializer.Serialize(new { kind = r.Kind, requestedBy = r.RequestedBy, comment }, Json), user.IpAddress, user.CorrelationId), ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private async Task<IResult> FailAsync(ChangeRequest r, string error, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        error = error.Length <= 2000 ? error : error[..1997] + "...";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.ChangeRequests.Where(x => x.Id == r.Id && x.Status == ChangeRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ChangeRequestStatus.Failed).SetProperty(x => x.DecidedBy, user.Login)
                .SetProperty(x => x.DecidedByName, user.Name).SetProperty(x => x.DecidedAt, now).SetProperty(x => x.Error, error), ct);
        if (changed == 0) return TypedResults.Problem("So'rov allaqachon hal qilingan", statusCode: 409);
        await audit.SaveChangesAsync(new AuditEntry(user.Login, user.Name, "change.failed", "change-request", r.Id.ToString(),
            JsonSerializer.Serialize(new { kind = r.Kind, requestedBy = r.RequestedBy, error }, Json), user.IpAddress, user.CorrelationId), ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Problem($"O'zgarishni bajarib bo'lmadi, hech narsa o'zgarmadi: {error}", statusCode: 409);
    }

    /// <summary>O'zgarish tasdiqlovchining o'ziga tegishlimi (o'ziga vakolat, o'ziga SoD istisnosi).</summary>
    private async Task<bool> TouchesApproverAsync(ChangeRequest r, CancellationToken ct)
    {
        switch (r.Kind)
        {
            case ChangeKinds.PortalRoleGrant or ChangeKinds.PortalRoleRevoke:
                return string.Equals(Parse<PortalRoleChange>(r.Payload).Login, user.Login, StringComparison.OrdinalIgnoreCase);
            case ChangeKinds.SodExceptionGrant:
                var me = await RequestEndpoints.ResolveIdentityAsync(db, user.Login, ct);
                return me is not null && Parse<SodExceptionBody>(r.Payload).IdentityId == me;
            default:
                return false;
        }
    }

    private async Task<IResult> TryExecuteAsync(string kind, string json, IAuditActor actor, CancellationToken ct)
    {
        try
        {
            return await ExecuteAsync(kind, json, actor, ct);
        }
        catch (DbUpdateException)
        {
            // Noyob indeks va h.k.: parallel o'zgarish
            return TypedResults.Problem("Ma'lumot shu orada o'zgargan, sahifani yangilab qayta urining", statusCode: 409);
        }
    }

    private async Task<IResult> ExecuteAsync(string kind, string json, IAuditActor actor, CancellationToken ct)
    {
        T Get<T>() where T : notnull => services.GetRequiredService<T>();
        switch (kind)
        {
            case ChangeKinds.PortalRoleGrant:
                return await GrantPortalRoleAsync(Parse<PortalRoleChange>(json), actor, ct);
            case ChangeKinds.PortalRoleRevoke:
                return await RevokePortalRoleAsync(Parse<PortalRoleChange>(json), actor, ct);
            case ChangeKinds.RoleEntitlements:
                var e = Parse<RoleEntitlementsChange>(json);
                return await AccessEndpoints.SetRoleEntitlementsAsync(e.RoleId, e.EntitlementIds ?? [], actor, db, audit, Get<AccessAnalyzer>(), time, ct);
            case ChangeKinds.RoleRuleAdd:
                return await AccessEndpoints.AddRoleRuleAsync(Parse<RoleRuleAddChange>(json), actor, db, audit, Get<AccessAnalyzer>(), time, ct);
            case ChangeKinds.RoleRuleRemove:
                var rr = Parse<RoleRuleRemoveChange>(json);
                return await AccessEndpoints.RemoveRoleRuleAsync(rr.RoleId, rr.RuleId, actor, db, audit, Get<AccessAnalyzer>(), time, ct);
            case ChangeKinds.RoleDelete:
                return await AccessEndpoints.DeleteRoleAsync(Parse<RoleDeleteChange>(json).RoleId, actor, db, audit, ct);
            case ChangeKinds.SodRuleCreate:
                return await AccessEndpoints.CreateSodRuleAsync(Parse<SaveSodRuleRequest>(json), actor, db, audit, time, ct);
            case ChangeKinds.SodRuleDelete:
                return await AccessEndpoints.DeleteSodRuleAsync(Parse<SodRuleDeleteChange>(json).RuleId, actor, db, audit, ct);
            case ChangeKinds.SodExceptionGrant:
                var identity = await RequestEndpoints.ResolveIdentityAsync(db, actor.Login, ct);
                var requestActor = new Bank.Idm.Infrastructure.Requests.RequestActor(actor.Login, actor.Name, identity, IsAdmin: true, actor.IpAddress, actor.CorrelationId);
                return await AccessEndpoints.GrantSodExceptionAsync(Parse<SodExceptionBody>(json), requestActor, Get<SodExceptionService>(), ct);
            case ChangeKinds.ConnectorMassDelete:
                return await StartMassDeleteAsync(Parse<MassDeleteChange>(json).Code, actor, ct);
            default:
                return TypedResults.Problem($"Noma'lum amal turi: {kind}", statusCode: 400);
        }
    }

    // ---------- Portal vakolatlari ----------

    private async Task<IResult> GrantPortalRoleAsync(PortalRoleChange change, IAuditActor actor, CancellationToken ct)
    {
        var login = change.Login?.Trim() ?? "";
        if (!LoginPattern().IsMatch(login))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["login"] = ["Login: lotin harflari, raqam, '.', '_', '-', '@' (ko'pi bilan 128 belgi)"] });
        if (!Enum.IsDefined(change.Role)) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Noma'lum vakolat"] });
        if (await db.PortalRoleAssignments.AnyAsync(p => p.Login == login && p.Role == change.Role, ct))
            return TypedResults.Conflict($"{login} da bu vakolat allaqachon bor");
        var assignment = new PortalRoleAssignment { Login = login, Role = change.Role, CreatedAt = time.GetUtcNow(), CreatedBy = actor.Login };
        db.PortalRoleAssignments.Add(assignment);
        await audit.SaveChangesAsync(new AuditEntry(actor.Login, actor.Name, "portal-role.granted", "portal-role", login,
            JsonSerializer.Serialize(new { login, role = change.Role.ToClaim() }, Json), actor.IpAddress, actor.CorrelationId), ct);
        return TypedResults.Ok(new PortalRoleItem(assignment.Id, login, change.Role, null, assignment.CreatedAt, assignment.CreatedBy));
    }

    private async Task<IResult> RevokePortalRoleAsync(PortalRoleChange change, IAuditActor actor, CancellationToken ct)
    {
        var login = change.Login?.Trim() ?? "";
        var assignment = await db.PortalRoleAssignments.FirstOrDefaultAsync(p => p.Login == login && p.Role == change.Role, ct);
        if (assignment is null) return TypedResults.NotFound();
        if (change.Role == PortalRole.Admin)
        {
            // Ikki kishilik tasdiq yoqilganda ikkitadan kam administrator qolsa, keyingi har qanday o'zgarishni tasdiqlaydigan odam bo'lmaydi
            var minimum = Enabled ? 2 : 1;
            var admins = await db.PortalRoleAssignments.CountAsync(p => p.Role == PortalRole.Admin, ct);
            if (admins - 1 < minimum)
                return TypedResults.Conflict($"Kamida {minimum} ta administrator qolishi kerak: avval yangi administrator qo'shing");
        }
        db.PortalRoleAssignments.Remove(assignment);
        await audit.SaveChangesAsync(new AuditEntry(actor.Login, actor.Name, "portal-role.revoked", "portal-role", login,
            JsonSerializer.Serialize(new { login, role = change.Role.ToClaim() }, Json), actor.IpAddress, actor.CorrelationId), ct);
        return TypedResults.NoContent();
    }

    // ---------- Ommaviy o'chirishni tasdiqlab sinxronlash ----------

    private async Task<IResult> CheckMassDeleteAsync(string json, CancellationToken ct)
    {
        var code = Parse<MassDeleteChange>(json).Code;
        if (!await db.Applications.AnyAsync(a => a.Code == code, ct)) return TypedResults.NotFound();
        return services.GetRequiredService<ConnectorSyncCoordinator>().IsConfigured(code)
            ? TypedResults.NoContent() : TypedResults.Problem($"{code} uchun konnektor sozlanmagan", statusCode: 400);
    }

    private async Task<IResult> StartMassDeleteAsync(string code, IAuditActor actor, CancellationToken ct)
    {
        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code, ct);
        if (app is null) return TypedResults.NotFound();
        var started = await services.GetRequiredService<ConnectorSyncCoordinator>().StartAsync(app.Code, ConnectorRunTrigger.Manual, actor.Login, actor.Name, true);
        switch (started.Status)
        {
            case SyncStartStatus.NotConfigured:
                return TypedResults.Problem($"{app.Code} uchun konnektor sozlanmagan", statusCode: 400);
            case SyncStartStatus.AlreadyRunning:
                return TypedResults.Problem($"{app.Code} sinxronlanmoqda, tugashini kuting", statusCode: 409);
        }
        await audit.SaveChangesAsync(new AuditEntry(actor.Login, actor.Name, "connector.mass-delete-approved", "application", app.Id.ToString(),
            JsonSerializer.Serialize(new { application = app.Code, runId = started.RunId }, Json), actor.IpAddress, actor.CorrelationId), ct);
        return TypedResults.Accepted($"/api/applications/{app.Code}/runs", new SyncStarted(started.RunId!.Value));
    }

    // ---------- Yordamchilar ----------

    internal static ChangeRequestItem ToItem(ChangeRequest r, DateTimeOffset now, string? me = null, bool isAdmin = false)
    {
        var expired = r.Status == ChangeRequestStatus.Pending && r.ExpiresAt <= now;
        var open = r.Status == ChangeRequestStatus.Pending && !expired;
        return new ChangeRequestItem(r.Id, r.Kind, expired ? "Expired" : r.Status.ToString(),
            JsonSerializer.Deserialize<List<PreviewEntry>>(r.Preview, Json) ?? [], r.RequestedBy, r.RequestedByName, r.RequestedAt, r.ExpiresAt,
            r.DecidedBy, r.DecidedByName, r.DecidedAt, r.DecisionComment, r.Error,
            CanDecide: open && isAdmin && me is not null && r.RequestedBy != me, CanCancel: open && r.RequestedBy == me);
    }

    private static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException("Bo'sh o'zgarish");

    private static bool IsSuccess(IResult result) => result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 };

    /// <summary>Muvaffaqiyatsiz natijadan odam o'qiydigan sabab.</summary>
    private static string Describe(IResult result) => result switch
    {
        ProblemHttpResult p => p.ProblemDetails.Detail ?? p.ProblemDetails.Title ?? "Xato",
        ValidationProblem v => string.Join("; ", v.ProblemDetails.Errors.SelectMany(x => x.Value)),
        Conflict<string> c => c.Value ?? "Ziddiyat",
        NotFound => "Obyekt topilmadi (o'chirilgan bo'lishi mumkin)",
        IStatusCodeHttpResult s => $"HTTP {s.StatusCode}",
        _ => "Xato"
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record PreviewEntry(string Action, string? Details);
