namespace Bank.Idm.Connectors.Ldap;

/// <summary>
/// LDAP katalogi sozlamalari. Standart qiymatlar Active Directory uchun; OpenLDAP uchun atribut va filtrlar almashtiriladi
/// (README'dagi namunaga qarang).
/// </summary>
public class LdapConnectorOptions
{
    public string Server { get; set; } = null!;
    public int Port { get; set; } = 636;
    /// <summary>LDAPS. Bankdagi AD oddiy LDAP'da parol bilan ulanishni rad etadi.</summary>
    public bool UseSsl { get; set; } = true;
    /// <summary>Xizmat akkaunti (faqat o'qish huquqi yetarli).</summary>
    public string BindDn { get; set; } = null!;
    public string BindPassword { get; set; } = null!;
    public int TimeoutSeconds { get; set; } = 60;
    /// <summary>Bir so'rovda olinadigan yozuvlar (AD'da MaxPageSize = 1000).</summary>
    public int PageSize { get; set; } = 500;

    public string UsersDn { get; set; } = null!;
    public string UserFilter { get; set; } = "(&(objectCategory=person)(objectClass=user))";
    /// <summary>Bloklangan akkauntlar: AD'da userAccountControl'ning ACCOUNTDISABLE (2) biti.</summary>
    public string? DisabledFilter { get; set; } = "(userAccountControl:1.2.840.113556.1.4.803:=2)";
    public string GroupsDn { get; set; } = null!;
    public string GroupFilter { get; set; } = "(objectClass=group)";

    /// <summary>O'zgarmas identifikator. objectGUID ikkilik qiymat sifatida o'qiladi va GUID matniga aylantiriladi.</summary>
    public string UserIdAttribute { get; set; } = "objectGUID";
    public string LoginAttribute { get; set; } = "sAMAccountName";
    public string EmployeeNumberAttribute { get; set; } = "employeeNumber";
    public string FirstNameAttribute { get; set; } = "givenName";
    public string LastNameAttribute { get; set; } = "sn";
    public string DisplayNameAttribute { get; set; } = "displayName";
    public string EmailAttribute { get; set; } = "mail";
    public string DepartmentAttribute { get; set; } = "department";
    public string JobTitleAttribute { get; set; } = "title";
    public string? ManagerAttribute { get; set; } = "manager";

    public string GroupIdAttribute { get; set; } = "objectGUID";
    public string GroupNameAttribute { get; set; } = "cn";
    public string GroupDescriptionAttribute { get; set; } = "description";
    /// <summary>Guruh a'zolari: AD'da "member" (DN ro'yxati), posixGroup'da "memberUid" (loginlar).</summary>
    public string GroupMemberAttribute { get; set; } = "member";
    /// <summary>true: a'zo qiymati foydalanuvchi DN'i; false: foydalanuvchi logini.</summary>
    public bool GroupMemberIsDn { get; set; } = true;
}
