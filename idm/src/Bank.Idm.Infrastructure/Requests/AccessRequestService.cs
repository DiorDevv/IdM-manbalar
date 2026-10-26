using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Access;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Requests;

/// <summary>Kim amal bajaryapti: SSO logini, unga bog'langan xodim (AD akkaunti orqali) va IdM administratorimi.</summary>
public record RequestActor(string Login, string? Name, Guid? IdentityId, bool IsAdmin, string? IpAddress = null, string? CorrelationId = null);

/// <summary>Xizmat natijasi: muvaffaqiyat yoki HTTP holati va foydalanuvchiga ko'rsatiladigan xabar.</summary>
/// <summary>Arizada saqlanadigan SoD ziddiyati (JSON).</summary>
public record SodConflictInfo(string RuleCode, string RuleName, string Risk, IReadOnlyList<string> Left, IReadOnlyList<string> Right);

public record RequestResult(AccessRequest? Request, int Status = 200, string? Error = null)
{
    public static RequestResult Fail(int status, string error) => new(null, status, error);
}

/// <summary>
/// Rolga ariza: xodim (yoki uning rahbari) so'raydi → rahbar → resurs (rol) egasi → SoD ziddiyati bo'lsa axborot xavfsizligi
/// (IdM administratorlari) tasdiqlaydi. Oxirgi tasdiqdan keyin rol beriladi ("bo'lishi kerak"), tizimlarda bajarilishi — G bosqichi.
/// Qoidalar: o'z arizasini hech kim tasdiqlay olmaydi; rahbar o'zi so'rasa, rahbar bosqichi avtomatik; tasdiqlovchi belgilanmagan bosqich
/// administratorlar navbatiga tushadi; har bosqich va natija auditda; parallel qarorlar optimistik qulf bilan rad etiladi.
/// </summary>
public class AccessRequestService(IdmDbContext db, AuditLog audit, AccessAnalyzer analyzer, TimeProvider time)
{
    public const int MinJustification = 10;
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Ariza rejasi: kim uchun, qaysi rol, SoD ziddiyatlari va tasdiqlash zanjiri (hali saqlanmagan).</summary>
    public record Plan(Identity Beneficiary, Role Role, IReadOnlyList<SodViolation> Conflicts, IReadOnlyList<(ApprovalStepKind Kind, Guid? ApproverId, bool Auto)> Steps);

    /// <summary>Yuborishdan oldingi tekshiruvlar (huquq, rol, takror ariza, SoD) va zanjir. Xato bo'lsa — Error.</summary>
    public async Task<(Plan? Plan, int Status, string? Error)> PlanAsync(RequestActor actor, Guid? beneficiaryId, Guid roleId, CancellationToken ct = default)
    {
        if (actor.IdentityId is not { } requesterId)
            return (null, 400, "Portal foydalanuvchingiz xodim yozuviga bog'lanmagan (AD akkaunti topilmadi). IdM administratoriga murojaat qiling.");
        var now = time.GetUtcNow();
        var beneficiary = await db.Identities.FirstOrDefaultAsync(i => i.Id == (beneficiaryId ?? requesterId), ct);
        if (beneficiary is null) return (null, 404, "Xodim topilmadi");
        if (beneficiary.Status == IdentityStatus.Terminated) return (null, 400, "Ishdan ketgan xodimga rol so'rab bo'lmaydi");
        if (beneficiary.Id != requesterId && beneficiary.ManagerId != requesterId && !actor.IsAdmin)
            return (null, 403, "Faqat o'zingiz yoki bevosita xodimlaringiz uchun so'rash mumkin");
        var role = await db.Roles.Include(r => r.Entitlements).FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null || !role.IsRequestable) return (null, 400, "Bu rolni ariza orqali so'rab bo'lmaydi");
        if (await db.IdentityRoles.AnyAsync(r => r.IdentityId == beneficiary.Id && r.RoleId == roleId && r.ValidFrom <= now && (r.ValidTo == null || r.ValidTo > now), ct))
            return (null, 409, "Bu rol xodimda allaqachon bor");
        if (await db.AccessRequests.AnyAsync(r => r.BeneficiaryId == beneficiary.Id && r.RoleId == roleId && r.Status == AccessRequestStatus.Pending, ct))
            return (null, 409, "Bu rol uchun ko'rib chiqilayotgan ariza bor");

        var conflicts = await analyzer.SodPreviewAsync(beneficiary.Id, role.Entitlements.Select(e => e.EntitlementId).ToList(), ct);
        // Tasdiqlovchi xodimning o'zi bo'la olmaydi (bunday holda — administratorlar navbati);
        // tasdiqlovchining o'zi so'rasa (rahbar xodimi uchun), shu bosqich so'rov bilan tasdiqlangan hisoblanadi
        Guid? Safe(Guid? approver) => approver == beneficiary.Id ? null : approver;
        var steps = new List<(ApprovalStepKind, Guid?, bool)>();
        var manager = Safe(beneficiary.ManagerId);
        steps.Add((ApprovalStepKind.Manager, manager, manager is not null && manager == requesterId));
        var owner = Safe(role.OwnerId);
        if (owner is null || owner != manager) steps.Add((ApprovalStepKind.ResourceOwner, owner, owner is not null && owner == requesterId));
        if (conflicts.Count > 0) steps.Add((ApprovalStepKind.Security, null, false));
        return (new Plan(beneficiary, role, conflicts, steps), 200, null);
    }

    public async Task<RequestResult> CreateAsync(RequestActor actor, Guid? beneficiaryId, Guid roleId, string? justification, DateTimeOffset? validTo,
        CancellationToken ct = default)
    {
        var text = justification?.Trim() ?? "";
        if (text.Length < MinJustification || text.Length > 2000)
            return RequestResult.Fail(400, $"Asos {MinJustification}–2000 belgi bo'lishi kerak: nima uchun bu ruxsat kerakligini yozing");
        var now = time.GetUtcNow();
        if (validTo is { } until && (until <= now || until > now.AddYears(1)))
            return RequestResult.Fail(400, "Amal qilish muddati kelajakda va ko'pi bilan 1 yil bo'lishi kerak");
        var (plan, status, error) = await PlanAsync(actor, beneficiaryId, roleId, ct);
        if (plan is null) return RequestResult.Fail(status, error!);

        var request = new AccessRequest
        {
            RequesterId = actor.IdentityId!.Value, BeneficiaryId = plan.Beneficiary.Id, RoleId = roleId, Justification = text, ValidTo = validTo, CreatedAt = now,
            SodConflicts = plan.Conflicts.Count == 0 ? null : JsonSerializer.Serialize(plan.Conflicts.Select(c =>
                new SodConflictInfo(c.RuleCode, c.RuleName, c.Risk.ToString(), c.LeftEntitlements, c.RightEntitlements)), Json)
        };
        foreach (var (kind, approver, auto) in plan.Steps)
        {
            var step = new Approval { Step = request.Approvals.Count + 1, Kind = kind, ApproverId = approver };
            if (auto) (step.Decision, step.DecidedAt, step.DecidedBy, step.Comment) = (ApprovalDecision.Approved, now, "system", "Arizani tasdiqlovchining o'zi yuborgan");
            request.Approvals.Add(step);
        }
        db.AccessRequests.Add(request);

        var entries = new List<AuditEntry>
        {
            Entry(actor, "request.created", request.Id, new
            {
                role = plan.Role.Code, beneficiary = plan.Beneficiary.EmployeeNumber, validTo, sod = plan.Conflicts.Select(c => c.RuleCode),
                steps = request.Approvals.Select(a => new { kind = a.Kind.ToString(), approver = a.ApproverId, auto = a.Decision == ApprovalDecision.Approved })
            })
        };
        if (request.Approvals.All(a => a.Decision == ApprovalDecision.Approved)) entries.AddRange(Grant(actor, request, plan.Role, plan.Beneficiary, now));
        await audit.SaveChangesAsync(entries, ct);
        return new RequestResult(request, 201);
    }

    public async Task<RequestResult> DecideAsync(RequestActor actor, Guid requestId, bool approve, string? comment, CancellationToken ct = default)
    {
        var request = await db.AccessRequests.Include(r => r.Approvals).Include(r => r.Role).Include(r => r.Beneficiary)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return RequestResult.Fail(404, "Ariza topilmadi");
        if (request.Status != AccessRequestStatus.Pending) return RequestResult.Fail(409, "Ariza allaqachon ko'rib chiqilgan");
        var step = request.Approvals.Where(a => a.Decision == ApprovalDecision.Pending).OrderBy(a => a.Step).First();
        if (!CanDecide(actor, request, step)) return RequestResult.Fail(403, "Bu bosqichni siz tasdiqlay olmaysiz");
        var text = comment?.Trim();
        if (!approve && (text is null || text.Length < 3)) return RequestResult.Fail(400, "Rad etish sababini yozing");
        if (text?.Length > 2000) return RequestResult.Fail(400, "Izoh ko'pi bilan 2000 belgi");

        var now = time.GetUtcNow();
        (step.Decision, step.Comment, step.DecidedAt, step.DecidedBy, step.DecidedByName) =
            (approve ? ApprovalDecision.Approved : ApprovalDecision.Rejected, text, now, actor.Login, actor.Name);
        var entries = new List<AuditEntry>
        {
            Entry(actor, approve ? "request.step-approved" : "request.rejected", request.Id,
                new { role = request.Role.Code, beneficiary = request.Beneficiary.EmployeeNumber, step = step.Kind.ToString(), comment = text })
        };
        if (!approve)
        {
            (request.Status, request.ClosedAt) = (AccessRequestStatus.Rejected, now);
        }
        else if (request.Approvals.All(a => a.Decision == ApprovalDecision.Approved))
        {
            entries.AddRange(Grant(actor, request, request.Role, request.Beneficiary, now));
        }
        db.Entry(request).State = EntityState.Modified;   // har qarorda arizaning versiyasi tekshiriladi
        try
        {
            await audit.SaveChangesAsync(entries, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RequestResult.Fail(409, "Ariza shu payt boshqa foydalanuvchi tomonidan o'zgartirildi. Sahifani yangilang.");
        }
        return new RequestResult(request);
    }

    public async Task<RequestResult> CancelAsync(RequestActor actor, Guid requestId, CancellationToken ct = default)
    {
        var request = await db.AccessRequests.Include(r => r.Role).FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return RequestResult.Fail(404, "Ariza topilmadi");
        if (request.Status != AccessRequestStatus.Pending) return RequestResult.Fail(409, "Faqat ko'rib chiqilayotgan arizani bekor qilish mumkin");
        if (request.RequesterId != actor.IdentityId && !actor.IsAdmin) return RequestResult.Fail(403, "Arizani faqat uni yuborgan xodim bekor qila oladi");
        (request.Status, request.ClosedAt) = (AccessRequestStatus.Cancelled, time.GetUtcNow());
        try
        {
            await audit.SaveChangesAsync(Entry(actor, "request.cancelled", request.Id, new { role = request.Role.Code }), ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RequestResult.Fail(409, "Ariza shu payt boshqa foydalanuvchi tomonidan o'zgartirildi. Sahifani yangilang.");
        }
        return new RequestResult(request);
    }

    /// <summary>Navbatdagi bosqichni shu foydalanuvchi hal qila oladimi: belgilangan tasdiqlovchi yoki (bo'sh bo'lsa) administrator; o'z arizasi emas.</summary>
    public static bool CanDecide(RequestActor actor, AccessRequest request, Approval step)
    {
        if (actor.IdentityId is { } me && (me == request.BeneficiaryId || me == request.RequesterId)) return false;
        return step.ApproverId is { } approver ? approver == actor.IdentityId : actor.IsAdmin;
    }

    private IEnumerable<AuditEntry> Grant(RequestActor actor, AccessRequest request, Role role, Identity beneficiary, DateTimeOffset now)
    {
        (request.Status, request.ClosedAt) = (AccessRequestStatus.Approved, now);
        db.IdentityRoles.Add(new IdentityRole
        {
            IdentityId = beneficiary.Id, RoleId = role.Id, Source = RoleAssignmentSource.Request, ValidFrom = now, ValidTo = request.ValidTo, AccessRequestId = request.Id
        });
        yield return Entry(actor, "request.approved", request.Id, new { role = role.Code, beneficiary = beneficiary.EmployeeNumber, request.ValidTo });
        yield return new AuditEntry(actor.Login, actor.Name, "identity.role-assigned", "identity", beneficiary.Id.ToString(),
            JsonSerializer.Serialize(new { role = role.Code, beneficiary.EmployeeNumber, source = "request", requestId = request.Id, request.ValidTo }, Json),
            actor.IpAddress, actor.CorrelationId);
    }

    private static AuditEntry Entry(RequestActor actor, string action, Guid requestId, object details) =>
        new(actor.Login, actor.Name, action, "access-request", requestId.ToString(), JsonSerializer.Serialize(details, Json), actor.IpAddress, actor.CorrelationId);
}
