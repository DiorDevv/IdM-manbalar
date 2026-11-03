using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Tests;

/// <summary>Xotiradagi pochta: yuborilganlarni yig'adi, kerak bo'lsa xato beradi.</summary>
public sealed class FakeSender : INotificationSender
{
    public List<OutgoingMail> Sent { get; } = [];
    public Exception? Error { get; set; }
    public Task SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        if (Error is not null) throw Error;
        Sent.Add(mail);
        return Task.CompletedTask;
    }
}

/// <summary>Karimov rahbari Rahimovaga ariza yuboradi; Rahimovaning o'rinbosari — Aliyev; Yusupovaning emaili yo'q.</summary>
public class NotificationTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly MutableTime _time = new(new DateTimeOffset(2026, 11, 3, 7, 0, 0, TimeSpan.Zero));
    private readonly FakeSender _mail = new();
    private static readonly NotificationOptions Options = new() { Enabled = true, PortalUrl = "https://idm.bank.uz/", ReminderAfterHours = 24, MaxReminders = 2 };
    private Guid _karimov, _rahimova, _aliyev, _request, _approval;

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using var db = pg.CreateContext();
        Identity Person(string n, string first, string last, string? email) => new() { EmployeeNumber = n, FirstName = first, LastName = last, Email = email };
        var rahimova = Person("T2", "Dilnoza", "Rahimova", "d.rahimova@bank.uz");
        var karimov = Person("T1", "Aziz", "Karimov", "a.karimov@bank.uz");
        karimov.Manager = rahimova;
        var aliyev = Person("T3", "Jasur", "Aliyev", "j.aliyev@bank.uz");
        var yusupova = Person("T4", "Nodira", "Yusupova", null);
        var role = new Role { Code = "KREDIT", Name = "Kredit mutaxassisi", Owner = yusupova };
        var request = new AccessRequest { Requester = karimov, Beneficiary = karimov, Role = role, Justification = "Kredit arizalari uchun kerak", CreatedAt = _time.Now };
        request.Approvals.Add(new Approval { Step = 1, Kind = ApprovalStepKind.Manager, Approver = rahimova });
        request.Approvals.Add(new Approval { Step = 2, Kind = ApprovalStepKind.ResourceOwner, Approver = yusupova });
        db.AddRange(rahimova, karimov, aliyev, yusupova, role, request,
            new ApproverDelegation { Delegator = rahimova, Delegate = aliyev, ValidFrom = _time.Now.AddDays(-1), ValidTo = _time.Now.AddDays(5), CreatedAt = _time.Now, CreatedBy = "d.rahimova" });
        await db.SaveChangesAsync(Ct);
        (_karimov, _rahimova, _aliyev, _request, _approval) = (karimov.Id, rahimova.Id, aliyev.Id, request.Id, request.Approvals.Single(a => a.Step == 1).Id);
    }

    public async ValueTask DisposeAsync() => await pg.DisposeAsync();

    private async Task<int> PlanAsync()
    {
        await using var db = pg.CreateContext();
        return await new NotificationService(db, _time).PlanAsync(Options, Ct);
    }

    private async Task<DispatchResult> DispatchAsync()
    {
        await using var db = pg.CreateContext();
        return await new NotificationService(db, _time).DispatchAsync(Options, _mail, Ct);
    }

    [Fact]
    public async Task Approver_and_delegate_are_told_once_then_reminded_and_the_requester_hears_the_result()
    {
        Assert.Equal(2, await PlanAsync());   // Rahimova va o'rinbosari Aliyev
        Assert.Equal(0, await PlanAsync());   // takror — yo'q
        Assert.Equal(new DispatchResult(2, 0, 0), await DispatchAsync());
        Assert.Equal(["d.rahimova@bank.uz", "j.aliyev@bank.uz"], _mail.Sent.Select(m => m.To).Order());
        var first = _mail.Sent[0];
        Assert.Contains("Kredit mutaxassisi", first.Subject);
        Assert.Contains($"https://idm.bank.uz/requests/{_request}", first.Body);
        Assert.DoesNotContain("Kredit arizalari uchun kerak", first.Body);   // asos xatga kirmaydi

        // 25 soat — 1-eslatma, 49 soat — 2-eslatma, keyin — yo'q (MaxReminders = 2)
        _time.Now = _time.Now.AddHours(25);
        Assert.Equal(2, await PlanAsync());
        _time.Now = _time.Now.AddHours(24);
        Assert.Equal(2, await PlanAsync());
        _time.Now = _time.Now.AddHours(24);
        Assert.Equal(0, await PlanAsync());

        // Rahbar tasdiqladi: keyingi bosqich egasi Yusupovaning emaili yo'q — xabar yozilmaydi. Rad etilgach — so'raganga natija
        await using (var db = pg.CreateContext())
        {
            var request = await db.AccessRequests.Include(r => r.Approvals).SingleAsync(r => r.Id == _request, Ct);
            var (manager, owner) = (request.Approvals.Single(a => a.Step == 1), request.Approvals.Single(a => a.Step == 2));
            (manager.Decision, manager.DecidedAt) = (ApprovalDecision.Approved, _time.Now);
            (owner.Decision, owner.DecidedAt) = (ApprovalDecision.Rejected, _time.Now);
            (request.Status, request.ClosedAt) = (AccessRequestStatus.Rejected, _time.Now);
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(1, await PlanAsync());
        await using (var db = pg.CreateContext())
        {
            var result = await db.Notifications.SingleAsync(n => n.Kind == "request-closed", Ct);
            Assert.Equal(("a.karimov@bank.uz", _karimov), (result.Email, result.RecipientId!.Value));
            Assert.Contains("rad etildi", result.Subject);
        }
    }

    [Fact]
    public async Task Failed_delivery_is_retried_with_backoff_and_given_up_after_max_attempts()
    {
        await PlanAsync();
        _mail.Error = new System.Net.Mail.SmtpException("server band");
        var options = Options;

        Assert.Equal(new DispatchResult(0, 1, 0), await DispatchAsync());   // birinchi xatoda to'plam to'xtaydi
        Assert.Equal(new DispatchResult(0, 1, 0), await DispatchAsync());   // birinchisining kechikishi tugamagan — ikkinchisi
        for (var i = 0; i < options.MaxAttempts * 3; i++)
        {
            _time.Now = _time.Now.AddHours(2);
            await DispatchAsync();
        }
        await using var db = pg.CreateContext();
        var all = await db.Notifications.ToListAsync(Ct);
        Assert.All(all, n => Assert.Equal((options.MaxAttempts, (DateTimeOffset?)null), (n.Attempts, n.SentAt)));
        Assert.All(all, n => Assert.NotNull(n.FailedAt));
        Assert.All(all, n => Assert.Equal("server band", n.LastError));
    }

    [Fact]
    public async Task Reviewers_hear_about_a_campaign_and_again_when_the_due_date_is_near()
    {
        await using (var db = pg.CreateContext())
        {
            var role = await db.Roles.SingleAsync(Ct);
            var assignment = new IdentityRole { IdentityId = _karimov, RoleId = role.Id, Source = RoleAssignmentSource.Request, ValidFrom = _time.Now };
            var campaign = new AccessReviewCampaign { Name = "IV chorak", DueDate = new DateOnly(2026, 11, 20), CreatedAt = _time.Now, CreatedBy = "boss" };
            campaign.Items.Add(new AccessReviewItem { Kind = AccessReviewItemKind.Role, IdentityId = _karimov, ReviewerId = _rahimova, IdentityRole = assignment, ApplicationCode = "IdM", Title = "Kredit", Subtitle = "KREDIT" });
            db.AddRange(assignment, campaign);
            // Arizani yopamiz: faqat attestatsiya xabarlari qolsin
            await db.AccessRequests.ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, AccessRequestStatus.Cancelled), Ct);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(2, await PlanAsync());   // Rahimova va o'rinbosari
        _time.Now = new DateTimeOffset(2026, 11, 17, 7, 0, 0, TimeSpan.Zero);   // muddatga 3 kun
        Assert.Equal(1, await PlanAsync());   // o'rinbosarlik tugagan — faqat Rahimova
        await using var check = pg.CreateContext();
        Assert.Equal(["review-due", "review-start", "review-start"], (await check.Notifications.Select(n => n.Kind).ToListAsync(Ct)).Order());
    }
}
