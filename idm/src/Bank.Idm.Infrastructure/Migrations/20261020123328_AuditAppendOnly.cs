using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Audit jurnali faqat qo'shiladi: UPDATE, DELETE va TRUNCATE baza darajasida taqiqlanadi.
            // (Jadval egasi triggerni o'chira oladi: buni xesh zanjiri aniqlaydi. Ishlab chiqarishda ilova
            // jadval egasi bo'lmagan, faqat INSERT/SELECT huquqli rol bilan ulanadi — I bosqichi.)
            migrationBuilder.Sql("""
                create or replace function audit_events_append_only() returns trigger language plpgsql as $$
                begin
                    raise exception 'audit_events is append-only: % is not allowed', tg_op using errcode = 'insufficient_privilege';
                end;
                $$;
                create trigger audit_events_no_update_delete before update or delete on audit_events
                    for each row execute function audit_events_append_only();
                create trigger audit_events_no_truncate before truncate on audit_events
                    for each statement execute function audit_events_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop trigger if exists audit_events_no_truncate on audit_events;
                drop trigger if exists audit_events_no_update_delete on audit_events;
                drop function if exists audit_events_append_only();
                """);
        }
    }
}
