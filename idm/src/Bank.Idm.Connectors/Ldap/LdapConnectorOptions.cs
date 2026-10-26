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

    // --- Yozish (G bosqichi). Faqat provisioning yoqilganda ishlatiladi ---

    /// <summary>
    /// Yozish uchun alohida xizmat akkaunti (eng kam huquq: faqat UsersDn/GroupsDn'da akkaunt yaratish, bloklash, a'zolik).
    /// Bo'sh bo'lsa — BindDn. Parol faqat muhit o'zgaruvchisi yoki sirlar saqlovchisidan.
    /// </summary>
    public string? WriteBindDn { get; set; }
    public string? WriteBindPassword { get; set; }
    /// <summary>Yangi akkauntlar yaratiladigan OU (UsersDn ichida bo'lishi shart). Bo'sh bo'lsa — UsersDn.</summary>
    public string? CreateDn { get; set; }
    /// <summary>
    /// Yangi akkaunt obyekt sinflari. Bo'sh bo'lsa — AD: top, person, organizationalPerson, user. Standart qiymat massivda emas:
    /// .NET konfiguratsiyasi massivni almashtirmaydi, indeks bo'yicha ustiga yozadi (CreateObjectClasses__0=inetOrgPerson qolgan AD sinflarini qoldirib ketardi).
    /// </summary>
    public string[] CreateObjectClasses { get; set; } = [];
    public static readonly string[] ActiveDirectoryObjectClasses = ["top", "person", "organizationalPerson", "user"];
    public string[] EffectiveObjectClasses => CreateObjectClasses.Length > 0 ? CreateObjectClasses : ActiveDirectoryObjectClasses;
    /// <summary>Yangi akkaunt DN'ining birinchi komponenti: AD'da "cn", OpenLDAP'da odatda "uid". Qiymati — login.</summary>
    public string RdnAttribute { get; set; } = "cn";
    /// <summary>AD: userPrincipalName = login@UpnSuffix (bo'sh bo'lsa yozilmaydi).</summary>
    public string? UpnSuffix { get; set; }
    /// <summary>AD sAMAccountName chegarasi — 20 belgi.</summary>
    public int LoginMaxLength { get; set; } = 20;
    /// <summary>
    /// Bloklash usuli. Bo'sh — AD: userAccountControl'ga ACCOUNTDISABLE (2) biti qo'yiladi.
    /// Boshqa kataloglar uchun: shu atributga DisableValue yoziladi (DisabledFilter bilan mos bo'lishi kerak).
    /// </summary>
    public string? DisableAttribute { get; set; }
    public string DisableValue { get; set; } = "disabled";
}
