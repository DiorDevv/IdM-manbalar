using Bank.Idm.Infrastructure;

namespace Bank.Idm.Tests;

public class NamingTests
{
    [Theory]
    [InlineData("AuditEvents", "audit_events")]
    [InlineData("EmployeeNumber", "employee_number")]
    [InlineData("IdentityId", "identity_id")]
    [InlineData("PK_AuditEvents", "pk_audit_events")]
    [InlineData("IpAddress", "ip_address")]
    public void Converts_names_to_snake_case(string input, string expected) => Assert.Equal(expected, IdmDbContext.ToSnakeCase(input));
}
