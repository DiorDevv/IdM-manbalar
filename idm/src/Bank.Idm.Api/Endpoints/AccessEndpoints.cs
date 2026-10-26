using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bank.Idm.Api.Security;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Api.Endpoints;

/// <summary>Rollar katalogi, avtomatik rol qoidalari, SoD qoidalari va ruxsatlar tahlili (E bosqichi).</summary>
public static partial class AccessEndpoints
{
    private const int MaxTake = 200;
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,63}$")]
    private static partial Regex CodePattern();

    public static void MapAccessEndpoints(this RouteGroupBuilder api)
    {
        var read = api.MapGroup("").RequireAuthorization(IdmEndpoints.CanReadAll);
        var admin = api.MapGroup("").RequireAuthorization(IdmEndpoints.CanAdminister);

        // ---------- Tanlash uchun ma'lumotnomalar ----------
        read.MapGet("/entitlements", async (IdmDbContext db, string? search, int take = 50, CancellationToken ct = default) =>
        {
            var query = db.Entitlements.AsNoTracking().Where(e => e.DeletedAt == null);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = $"%{search.Trim()}%";
                query = query.Where(e => EF.Functions.ILike(e.Name, s) || EF.Functions.ILike(e.Application.Code, s));
            }
            return await query.OrderBy(e => e.Application.Code).ThenBy(e => e.Name).Take(Math.Clamp(take, 1, MaxTake))
                .Select(e => new EntitlementPick(e.Id, e.Application.Code, e.Name, e.IsPrivileged, e.Risk)).ToListAsync(ct);
        });

        read.MapGet("/identity-attributes", async (IdmDbContext db, CancellationToken ct) => new AttributeValues(
            await db.Identities.Where(i => i.Department != null).Select(i => i.Department!).Distinct().OrderBy(x => x).Take(500).ToListAsync(ct),
            await db.Identities.Where(i => i.JobTitle != null).Select(i => i.JobTitle!).Distinct().OrderBy(x => x).Take(500).ToListAsync(ct)));

        // ---------- Rollar ----------
        read.MapGet("/roles", async (IdmDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var now = time.GetUtcNow();
            return await db.Roles.AsNoTracking().OrderBy(r => r.Name)
                .Select(r => new RoleListItem(r.Id, r.Code, r.Name, r.Description, r.Risk, r.IsRequestable, r.Entitlements.Count,
                    db.IdentityRoles.Where(ir => ir.RoleId == r.Id && ir.ValidFrom <= now && (ir.ValidTo == null || ir.ValidTo > now)).Select(ir => ir.IdentityId).Distinct().Count(),
                    r.Rules.Count))
                .ToListAsync(ct);
        });

        read.MapGet("/roles/{id:guid}", async Task<Results<Ok<RoleDetails>, NotFound>> (Guid id, IdmDbContext db, AccessAnalyzer analyzer, TimeProvider time, CancellationToken ct) =>
        {
            var details = await LoadRoleAsync(db, analyzer, time, id, ct);
            return details is null ? TypedResults.NotFound() : TypedResults.Ok(details);
        });

        admin.MapPost("/roles", async Task<Results<Created<RoleDetails>, ValidationProblem, Conflict<string>>> (SaveRoleRequest request, IdmDbContext db,
            AuditLog audit, CurrentUser user, AccessAnalyzer analyzer, TimeProvider time, CancellationToken ct) =>
        {
            var errors = Validate(request.Code, request.Name, request.Description);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var code = request.Code.Trim();
            if (await db.Roles.AnyAsync(r => r.Code == code, ct)) return TypedResults.Conflict($"{code} kodli rol allaqachon bor");
            var role = new Role
            {
                Code = code, Name = request.Name.Trim(), Description = Clean(request.Description), Risk = request.Risk,
                IsRequestable = request.IsRequestable, CreatedAt = time.GetUtcNow()
            };
            db.Roles.Add(role);
            await audit.SaveChangesAsync(Entry(user, "role.created", role.Id, new { role.Code, role.Name, risk = role.Risk.ToString(), role.IsRequestable }), ct);
            return TypedResults.Created($"/api/roles/{role.Id}", (await LoadRoleAsync(db, analyzer, time, role.Id, ct))!);
        });

        admin.MapPut("/roles/{id:guid}", async Task<Results<Ok<RoleDetails>, NotFound, ValidationProblem, Conflict<string>>> (Guid id, SaveRoleRequest request,
            IdmDbContext db, AuditLog audit, CurrentUser user, AccessAnalyzer analyzer, TimeProvider time, CancellationToken ct) =>
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return TypedResults.NotFound();
            var errors = Validate(request.Code, request.Name, request.Description);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var code = request.Code.Trim();
            if (code != role.Code && await db.Roles.AnyAsync(r => r.Code == code, ct)) return TypedResults.Conflict($"{code} kodli rol allaqachon bor");
            var before = new { role.Code, role.Name, role.Description, risk = role.Risk.ToString(), role.IsRequestable };
            (role.Code, role.Name, role.Description, role.Risk, role.IsRequestable) = (code, request.Name.Trim(), Clean(request.Description), request.Risk, request.IsRequestable);
            await audit.SaveChangesAsync(Entry(user, "role.updated", role.Id, new
            {
                before, after = new { role.Code, role.Name, role.Description, risk = role.Risk.ToString(), role.IsRequestable }
            }), ct);
            return TypedResults.Ok((await LoadRoleAsync(db, analyzer, time, id, ct))!);
        });

        admin.MapDelete("/roles/{id:guid}", async Task<Results<NoContent, NotFound, Conflict<string>>> (Guid id, IdmDbContext db, AuditLog audit, CurrentUser user, CancellationToken ct) =>
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return TypedResults.NotFound();
            // Tarixni yo'qotmaslik uchun: kimgadir berilgan yoki arizada qatnashgan rol o'chirilmaydi
            if (await db.IdentityRoles.AnyAsync(r => r.RoleId == id, ct) || await db.AccessRequests.AnyAsync(r => r.RoleId == id, ct))
                return TypedResults.Conflict("Rol xodimlarga berilgan yoki arizalarda bor: o'chirib bo'lmaydi. Avval qoidalarini olib tashlang va qayta hisoblang.");
            db.Roles.Remove(role);
            await audit.SaveChangesAsync(Entry(user, "role.deleted", role.Id, new { role.Code, role.Name }), ct);
            return TypedResults.NoContent();
        });

        admin.MapPut("/roles/{id:guid}/entitlements", async Task<Results<Ok<RoleDetails>, NotFound, ValidationProblem>> (Guid id, SetRoleEntitlementsRequest request,
            IdmDbContext db, AuditLog audit, CurrentUser user, AccessAnalyzer analyzer, TimeProvider time, CancellationToken ct) =>
        {
            var role = await db.Roles.Include(r => r.Entitlements).FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return TypedResults.NotFound();
            var wanted = request.EntitlementIds.Distinct().ToList();
            if (wanted.Count > 500) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["entitlementIds"] = ["Ko'pi bilan 500 ta ruxsat"] });
            var existing = await db.Entitlements.Where(e => wanted.Contains(e.Id) && e.DeletedAt == null).Select(e => new { e.Id, e.Name, e.Application.Code }).ToListAsync(ct);
            if (existing.Count != wanted.Count)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["entitlementIds"] = ["Ruxsat topilmadi yoki tizimda o'chirilgan"] });
            var names = existing.ToDictionary(e => e.Id, e => $"{e.Code}: {e.Name}");
            var current = role.Entitlements.Select(e => e.EntitlementId).ToHashSet();
            var added = wanted.Where(e => !current.Contains(e)).ToList();
            var removed = role.Entitlements.Where(e => !wanted.Contains(e.EntitlementId)).ToList();
            if (added.Count == 0 && removed.Count == 0) return TypedResults.Ok((await LoadRoleAsync(db, analyzer, time, id, ct))!);
            foreach (var e in removed) role.Entitlements.Remove(e);
            foreach (var e in added) role.Entitlements.Add(new RoleEntitlement { RoleId = id, EntitlementId = e });
            var removedNames = await db.Entitlements.Where(e => removed.Select(r => r.EntitlementId).Contains(e.Id)).Select(e => e.Application.Code + ": " + e.Name).ToListAsync(ct);
            await audit.SaveChangesAsync(Entry(user, "role.entitlements-changed", id, new { role.Code, added = added.Select(a => names[a]), removed = removedNames }), ct);
            return TypedResults.Ok((await LoadRoleAsync(db, analyzer, time, id, ct))!);
        });

        admin.MapPost("/roles/{id:guid}/rules", async Task<Results<Ok<RoleDetails>, NotFound, ValidationProblem, Conflict<string>>> (Guid id, AddRoleRuleRequest request,
            IdmDbContext db, AuditLog audit, CurrentUser user, AccessAnalyzer analyzer, TimeProvider time, CancellationToken ct) =>
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return TypedResults.NotFound();
            var (department, title) = (Clean(request.Department), Clean(request.JobTitle));
            // Ikkalasi bo'sh qoida — butun bankka rol: xato bilan berilmasligi uchun taqiqlanadi
            if (department is null && title is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["department"] = ["Bo'lim yoki lavozimdan kamida bittasi kerak"] });
            if (department?.Length > 256 || title?.Length > 256)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["department"] = ["Ko'pi bilan 256 belgi"] });
            var rules = await db.RoleRules.Where(r => r.RoleId == id).ToListAsync(ct);
            if (rules.Any(r => BirthrightService.Matches(r.Department, department) && BirthrightService.Matches(department, r.Department)
                    && BirthrightService.Matches(r.JobTitle, title) && BirthrightService.Matches(title, r.JobTitle)))
                return TypedResults.Conflict("Bunday qoida allaqachon bor");
            var rule = new RoleRule { RoleId = id, Department = department, JobTitle = title, CreatedAt = time.GetUtcNow(), CreatedBy = user.Login };
            db.RoleRules.Add(rule);
            await audit.SaveChangesAsync(Entry(user, "role.rule-added", id, new { role.Code, department, jobTitle = title }), ct);
            return TypedResults.Ok((await LoadRoleAsync(db, analyzer, time, id, ct))!);
        });

        admin.MapDelete("/roles/{id:guid}/rules/{ruleId:guid}", async Task<Results<Ok<RoleDetails>, NotFound>> (Guid id, Guid ruleId, IdmDbContext db, AuditLog audit,
            CurrentUser user, AccessAnalyzer analyzer, TimeProvider time, CancellationToken ct) =>
        {
            var rule = await db.RoleRules.Include(r => r.Role).FirstOrDefaultAsync(r => r.Id == ruleId && r.RoleId == id, ct);
            if (rule is null) return TypedResults.NotFound();
            db.RoleRules.Remove(rule);
            await audit.SaveChangesAsync(Entry(user, "role.rule-removed", id, new { rule.Role.Code, rule.Department, rule.JobTitle }), ct);
            return TypedResults.Ok((await LoadRoleAsync(db, analyzer, time, id, ct))!);
        });

        // Qoidalar o'zgargach: kimga rol berilishi va kimdan olinishi darhol hisoblanadi (keyingi sinxronlashda ham avtomatik)
        admin.MapPost("/roles/recalculate", async (BirthrightService birthright, CurrentUser user, CancellationToken ct) =>
            await birthright.RecalculateAsync(user.Login, user.Name, ct));

        // ---------- Xodim: rollar va ruxsatlar farqi ----------
        read.MapGet("/identities/{id:guid}/access", async Task<Results<Ok<IdentityAccess>, NotFound>> (Guid id, IdmDbContext db, AccessAnalyzer analyzer,
            TimeProvider time, CancellationToken ct) =>
        {
            if (!await db.Identities.AnyAsync(i => i.Id == id, ct)) return TypedResults.NotFound();
            var now = time.GetUtcNow();
            var roles = await db.IdentityRoles.AsNoTracking().Where(r => r.IdentityId == id && r.ValidFrom <= now && (r.ValidTo == null || r.ValidTo > now))
                .OrderBy(r => r.Role.Name).Select(r => new IdentityRoleItem(r.RoleId, r.Role.Code, r.Role.Name, r.Source, r.ValidFrom)).ToListAsync(ct);
            var sod = await ToItemsAsync(db, await analyzer.SodViolationsAsync(id, ct), ct);
            return TypedResults.Ok(new IdentityAccess(roles, await analyzer.ForIdentityAsync(id, ct), sod));
        });

        read.MapGet("/reports/access-drift", async (IdmDbContext db, AccessAnalyzer analyzer, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            (skip, take) = (Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake));
            var drift = await analyzer.DriftAsync(ct);
            var ids = drift.Select(d => d.IdentityId).ToList();
            var people = await db.Identities.AsNoTracking().Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, Name = i.LastName + " " + i.FirstName, i.EmployeeNumber, i.Department, i.Status }).ToDictionaryAsync(i => i.Id, ct);
            var items = drift.Where(d => people.ContainsKey(d.IdentityId))
                .Select(d => { var p = people[d.IdentityId]; return new DriftItem(d.IdentityId, p.Name, p.EmployeeNumber, p.Department, p.Status, d.Missing, d.Excess); })
                .OrderByDescending(d => d.Missing).ThenByDescending(d => d.Excess).ThenBy(d => d.Name).ToList();
            return new PagedResult<DriftItem>(items.Skip(skip).Take(take).ToList(), items.Count, skip, take);
        });

        // ---------- SoD ----------
        read.MapGet("/sod/rules", async (IdmDbContext db, AccessAnalyzer analyzer, CancellationToken ct) =>
        {
            var counts = (await analyzer.SodViolationsAsync(null, ct)).GroupBy(v => v.RuleId).ToDictionary(g => g.Key, g => g.Count());
            var rules = await db.SodRules.AsNoTracking().Include(r => r.Entitlements).ThenInclude(e => e.Entitlement).ThenInclude(e => e.Application)
                .OrderByDescending(r => r.Risk).ThenBy(r => r.Code).ToListAsync(ct);
            return rules.Select(r => new SodRuleItem(r.Id, r.Code, r.Name, r.Description, r.Risk,
                r.Entitlements.Where(e => e.Side == SodSide.Left).Select(e => Pick(e.Entitlement)).ToList(),
                r.Entitlements.Where(e => e.Side == SodSide.Right).Select(e => Pick(e.Entitlement)).ToList(),
                counts.GetValueOrDefault(r.Id), r.CreatedAt, r.CreatedBy)).ToList();
        });

        read.MapGet("/sod/violations", async (IdmDbContext db, AccessAnalyzer analyzer, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            (skip, take) = (Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake));
            var all = await ToItemsAsync(db, await analyzer.SodViolationsAsync(null, ct), ct);
            return new PagedResult<SodViolationItem>(all.Skip(skip).Take(take).ToList(), all.Count, skip, take);
        });

        admin.MapPost("/sod/rules", async Task<Results<Created<SodRuleItem>, ValidationProblem, Conflict<string>>> (SaveSodRuleRequest request, IdmDbContext db,
            AuditLog audit, CurrentUser user, TimeProvider time, CancellationToken ct) =>
        {
            var errors = Validate(request.Code, request.Name, request.Description);
            var left = request.Left.Distinct().ToList();
            var right = request.Right.Distinct().ToList();
            if (left.Count == 0 || right.Count == 0) errors["left"] = ["Ikkala tomonda kamida bittadan ruxsat bo'lishi kerak"];
            else if (left.Intersect(right).Any()) errors["right"] = ["Bir ruxsat ikkala tomonda bo'lishi mumkin emas"];
            else if (left.Count + right.Count > 200) errors["left"] = ["Ko'pi bilan 200 ta ruxsat"];
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var all = left.Concat(right).ToList();
            var found = await db.Entitlements.Include(e => e.Application).Where(e => all.Contains(e.Id) && e.DeletedAt == null).ToListAsync(ct);
            if (found.Count != all.Count) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["left"] = ["Ruxsat topilmadi yoki tizimda o'chirilgan"] });
            var code = request.Code.Trim();
            if (await db.SodRules.AnyAsync(r => r.Code == code, ct)) return TypedResults.Conflict($"{code} kodli SoD qoidasi allaqachon bor");
            var rule = new SodRule { Code = code, Name = request.Name.Trim(), Description = Clean(request.Description), Risk = request.Risk, CreatedAt = time.GetUtcNow(), CreatedBy = user.Login };
            rule.Entitlements.AddRange(left.Select(e => new SodRuleEntitlement { EntitlementId = e, Side = SodSide.Left }));
            rule.Entitlements.AddRange(right.Select(e => new SodRuleEntitlement { EntitlementId = e, Side = SodSide.Right }));
            db.SodRules.Add(rule);
            var byId = found.ToDictionary(e => e.Id);
            await audit.SaveChangesAsync(new AuditEntry(user.Login, user.Name, "sod.rule-created", "sod-rule", rule.Id.ToString(), JsonSerializer.Serialize(new
            {
                rule.Code, rule.Name, risk = rule.Risk.ToString(),
                left = left.Select(e => $"{byId[e].Application.Code}: {byId[e].Name}"), right = right.Select(e => $"{byId[e].Application.Code}: {byId[e].Name}")
            }, Json), user.IpAddress, user.CorrelationId), ct);
            return TypedResults.Created($"/api/sod/rules/{rule.Id}", new SodRuleItem(rule.Id, rule.Code, rule.Name, rule.Description, rule.Risk,
                left.Select(e => Pick(byId[e])).ToList(), right.Select(e => Pick(byId[e])).ToList(), 0, rule.CreatedAt, rule.CreatedBy));
        });

        admin.MapDelete("/sod/rules/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, IdmDbContext db, AuditLog audit, CurrentUser user, CancellationToken ct) =>
        {
            var rule = await db.SodRules.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rule is null) return TypedResults.NotFound();
            db.SodRules.Remove(rule);
            await audit.SaveChangesAsync(new AuditEntry(user.Login, user.Name, "sod.rule-deleted", "sod-rule", id.ToString(),
                JsonSerializer.Serialize(new { rule.Code, rule.Name }, Json), user.IpAddress, user.CorrelationId), ct);
            return TypedResults.NoContent();
        });
    }

    private static async Task<RoleDetails?> LoadRoleAsync(IdmDbContext db, AccessAnalyzer analyzer, TimeProvider time, Guid id, CancellationToken ct)
    {
        var role = await db.Roles.AsNoTracking().Include(r => r.Entitlements).ThenInclude(e => e.Entitlement).ThenInclude(e => e.Application)
            .Include(r => r.Rules).AsSplitQuery().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return null;
        var now = time.GetUtcNow();
        var members = await db.IdentityRoles.AsNoTracking().Where(r => r.RoleId == id && r.ValidFrom <= now && (r.ValidTo == null || r.ValidTo > now))
            .OrderBy(r => r.Identity.LastName).ThenBy(r => r.Identity.FirstName).Take(500)
            .Select(r => new RoleMemberItem(r.IdentityId, r.Identity.LastName + " " + r.Identity.FirstName, r.Identity.EmployeeNumber, r.Identity.Department,
                r.Identity.Status, r.Source, r.ValidFrom)).ToListAsync(ct);
        var people = await db.Identities.AsNoTracking().Where(i => i.Status != IdentityStatus.Terminated).Select(i => new { i.Department, i.JobTitle }).ToListAsync(ct);
        var rules = role.Rules.OrderBy(r => r.Department).ThenBy(r => r.JobTitle)
            .Select(r => new RoleRuleItem(r.Id, r.Department, r.JobTitle,
                people.Count(p => BirthrightService.Matches(r.Department, p.Department) && BirthrightService.Matches(r.JobTitle, p.JobTitle)), r.CreatedAt, r.CreatedBy))
            .ToList();
        var entitlements = role.Entitlements.Select(e => e.Entitlement).OrderBy(e => e.Application.Code).ThenBy(e => e.Name).Select(Pick).ToList();
        var conflicts = await analyzer.RoleConflictsAsync(entitlements.Select(e => e.Id).ToList(), ct);
        return new RoleDetails(role.Id, role.Code, role.Name, role.Description, role.Risk, role.IsRequestable, role.CreatedAt, entitlements, rules, members, conflicts);
    }

    private static async Task<List<SodViolationItem>> ToItemsAsync(IdmDbContext db, IReadOnlyList<SodViolation> violations, CancellationToken ct)
    {
        var ids = violations.Select(v => v.IdentityId).Distinct().ToList();
        var people = await db.Identities.AsNoTracking().Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, Name = i.LastName + " " + i.FirstName, i.EmployeeNumber, i.Department }).ToDictionaryAsync(i => i.Id, ct);
        return violations.Where(v => people.ContainsKey(v.IdentityId)).Select(v =>
        {
            var p = people[v.IdentityId];
            return new SodViolationItem(v.RuleId, v.RuleCode, v.RuleName, v.Risk, v.IdentityId, p.Name, p.EmployeeNumber, p.Department, v.LeftEntitlements, v.RightEntitlements);
        }).ToList();
    }

    private static EntitlementPick Pick(Entitlement e) => new(e.Id, e.Application.Code, e.Name, e.IsPrivileged, e.Risk);

    private static AuditEntry Entry(CurrentUser user, string action, Guid roleId, object details) =>
        new(user.Login, user.Name, action, "role", roleId.ToString(), JsonSerializer.Serialize(details, Json), user.IpAddress, user.CorrelationId);

    private static Dictionary<string, string[]> Validate(string? code, string? name, string? description)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(code) || !CodePattern().IsMatch(code.Trim()))
            errors["code"] = ["Kod: 2–64 belgi, lotin harflari, raqam, nuqta, chiziqcha"];
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Majburiy maydon"];
        else if (name.Trim().Length > 256) errors["name"] = ["Ko'pi bilan 256 belgi"];
        if (description?.Length > 2000) errors["description"] = ["Ko'pi bilan 2000 belgi"];
        return errors;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
