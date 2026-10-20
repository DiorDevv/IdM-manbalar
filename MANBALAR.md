# IdM uchun ochiq C# manbalar

Bu papkada bank IdM tizimini qurishda asos yoki namuna bo'ladigan ochiq kodli C# loyihalar to'plangan.
Har biri GitHub'dan `git clone --depth 1` bilan yuklangan (2026-10-19).

Muhim: tayyor, to'liq C# IdM tizimi (midPoint yoki Syncope kabi) ochiq kodda yo'q.
Mavjud ochiq IdM'lar Java'da (midPoint, Syncope, OpenIDM). C#'da esa alohida qismlar bor.
Shuning uchun bu yerda IdM'ning har bir moduli uchun eng yaxshi C# namunalar yig'ilgan.

## Loyihalar

| Papka | Loyiha | Nima beradi (IdM rejasidagi qaysi bandga) | .NET | Litsenziya | Oxirgi commit |
|---|---|---|---|---|---|
| 01 | [SimpleIdServer](https://github.com/simpleidserver/SimpleIdServer) | Eng katta va faol .NET loyiha: SCIM server, **LDAP/AD'dan provisioning** (`src/IdServer/SimpleIdServer.IdServer.Provisioning.LDAP`), rollar, audit. Arxitektura uchun asosiy namuna (1, 5, 6-bandlar) | net10.0 | Apache-2.0 | 2026-03 |
| 01 | [SCIMReferenceCode](https://github.com/AzureAD/SCIMReferenceCode) | Microsoft'ning SCIM endpoint namunasi (Entra ID bilan akkaunt almashish) | netcoreapp3.1 (eski) | MIT | 2026-10 |
| 01 | [SCIM.Client](https://github.com/Authentick/SCIM.Client) | Boshqa tizimlarga SCIM orqali akkaunt yuborish uchun klient | net5.0 (eski) | MIT | 2021-01 |
| 02 | [OpenICF.Net](https://github.com/OpenIdentityPlatform/OpenICF.Net) | Konnektor freymvorki (OpenIDM/midPoint ishlatadigan standart), AD va PowerShell konnektorlari bor. "Har bir tizim alohida modul" g'oyasi (1-band, DI) | .NET Framework 4.5.2 (eski) | CDDL | 2025-08 |
| 02 | [ecma2-framework](https://github.com/lithnet/ecma2-framework) | Zamonaviy, ko'p oqimli konnektor freymvorki (DI, async), MIM/Entra ECMA host uchun | net8.0 | **ko'rsatilmagan** | 2026-07 |
| 02 | [googleapps-managementagent](https://github.com/lithnet/googleapps-managementagent) | To'liq ishlaydigan konnektor misoli: import/export, delta, xatolar bilan ishlash | net472 | MIT | 2026-06 |
| 03 | [MIMWAL](https://github.com/microsoft/MIMWAL) | Microsoft Identity Manager uchun workflow faoliyatlari: tasdiqlash, shartlar (3-bosqich: ariza portali) | .NET 3.5 (eski) | MIT | 2023-06 |
| 03 | [resourcemanagement-client](https://github.com/lithnet/resourcemanagement-client) | MIM Service bilan ishlovchi .NET klient | net8.0-windows | MIT | 2026-07 |
| 03 | [miis-autosync](https://github.com/lithnet/miis-autosync) | Sinxronizatsiyani rejalashtirish, navbat va qayta urinish (5-band: chidamlilik) | net48 | MIT | 2026-06 |
| 04 | [Novell.Directory.Ldap.NETStandard](https://github.com/dsbenghe/Novell.Directory.Ldap.NETStandard) | Kross-platform LDAP klient: LDAPS, paging, Linux'da ishlaydi (2-band) | net6/8/9 | MIT | 2026-09 |
| 04 | [pingcastle](https://github.com/netwrix/pingcastle) | AD xavfsizlik auditi: AD'ni o'qish, `userAccountControl`, nested group, imtiyozli akkauntlar tahlili (2, 3-bandlar) | net10.0 | **Non-Profit OSL 3.0** | 2026-10 |
| 04 | [ad-password-protection](https://github.com/lithnet/ad-password-protection) | AD parol siyosatini kuchaytirish (3-band: xavfsizlik) | net472 | MIT | 2026-10 |
| 05 | [AuthPermissions.AspNetCore](https://github.com/JonPSmith/AuthPermissions.AspNetCore) | ASP.NET Core'da qat'iy rol va ruxsatlar tizimi, EF Core (portal uchun, 3, 6-bandlar) | net6/10 | MIT | 2025-12 |
| 05 | [SelfService](https://github.com/Allenvh/SelfService) | ASP.NET Core portal: AD bilan ishlash, ruxsat berilgan/taqiqlangan guruhlar | net8.0 | **ko'rsatilmagan** | 2026-10 |
| 05 | [ADAuth](https://github.com/rcilogic/ADAuth) | ASP.NET Core'da AD orqali autentifikatsiya, kichik misol | net6.0 | Apache-2.0 | 2023-12 |
| 05 | [external-identities-approvals](https://github.com/Azure-Samples/active-directory-dotnet-external-identities-api-connectors-approvals) | Ariza va tasdiqlash ilovasi namunasi (approve/deny) | netcoreapp3.1 (eski) | MIT | 2020-06 |

## Litsenziya bo'yicha ogohlantirish (bank uchun muhim)

- **pingcastle**: Non-Profit OSL 3.0. Tijorat tashkilotida (bankda) kodini olib ishlatish uchun alohida litsenziya kerak. Faqat o'rganish va g'oya olish uchun.
- **ecma2-framework**, **SelfService**: litsenziya fayli yo'q, ya'ni rasmiy ruxsat berilmagan. Kodini ko'chirib olmang, faqat o'rganing.
- **OpenICF.Net**: CDDL, o'zgartirilgan fayllar ochiq bo'lishi shart.
- Qolganlari MIT yoki Apache-2.0: bemalol ishlatsa bo'ladi (mualliflik izohini saqlab).

## NuGet orqali ulanadigan kutubxonalar (yuklab olinmagan)

Bular kod sifatida emas, paket sifatida ulanadi: `System.DirectoryServices.Protocols`,
`System.DirectoryServices.AccountManagement`, `Microsoft.Graph`, `EF Core`, `Hangfire`/`Quartz.NET`,
`Elsa Workflows`, `Polly`, `Audit.NET`, `Serilog`, `ClosedXML`, `QuestPDF`, `FluentValidation`.

## SimpleIdServer'ni ishga tushirish (sinalgan, 2026-10-19)

Talab: .NET 10 SDK. Baza: `./install-postgres.sh` bilan PostgreSQL (Docker), aks holda xotirada (`INMEMORY`, restartda tozalanadi).

| Xizmat | Manzil | Papka |
|---|---|---|
| IdServer (login, OIDC, provisioning) | https://localhost:5001/master | `src/IdServer/SimpleIdServer.IdServer.Startup` |
| Admin panel | https://localhost:5002/master/users | `src/IdServer/SimpleIdServer.IdServer.Website.Startup` |
| SCIM server | https://localhost:5003/Schemas | `src/Scim/SimpleIdServer.Scim.Startup` |

Kirish: `administrator` / `password`. Har bir papkada:

```
dotnet build -p:LibraryRestore=false
dotnet run --no-build
```

Yuklangandan keyin qilingan o'zgarishlar (kodga tegilmagan):
- `git submodule update --init` bilan `json-ld.net` yuklandi (busiz build bo'lmaydi).
- jquery, bootstrap, popper, fontawesome npm'dan olinib `wwwroot/lib/` ga qo'yildi
  (cdnjs bu muhitda yopiq bo'lgani uchun; oddiy kompyuterda `libman` o'zi yuklaydi va `-p:LibraryRestore=false` shart emas).

Skrinshotlar: `skrinshotlar/` papkasida.

## Sinov LDAP + SimpleIdServer (AD'dan o'qish bosqichi)

Papka: `sinov-ldap/`. Rejadagi 1-bosqich ("faqat o'qish"): LDAP'dagi xodim va guruhlar IdServer'ga import qilinadi, LDAP'ga hech narsa yozilmaydi.

- **LDAP server**: OpenLDAP (`slapd`), `ldap://localhost:389`, baza `dc=bank,dc=local`, admin `cn=admin,dc=bank,dc=local` / `TestAdmin123`.
- **Sinov ma'lumotlari**: `sinov-ldap/bank-test-data.ldif`: 8 xodim (`ou=people`, tabel raqami `employeeNumber`, bo'lim, lavozim), 4 guruh (`ou=groups`: kredit-bolimi, buxgalteriya, it-adminlar, abs-operatorlar). Xodimlar paroli: `Parol123!`.
- **Ishga tushirish**:
  ```
  ./start-all.sh                      # serverlar (LDAP sozlamalari bilan)
  sinov-ldap/sync-ldap.sh             # mapping'ni sozlaydi va extract + import qiladi
  ```
- **Natija**: 8 xodim va 4 guruh import qilindi. Login = `uid`, ism/familiya/email to'ldirildi, `tabel_raqami`, `bolim`, `lavozim`, `LDAP_DISTINGUISHEDNAME` claim sifatida saqlandi, guruh a'zoliklari LDAP'dagidek.
  Admin panel: https://localhost:5002/master/users (Source ustunida `LDAP`).

### Ishga tushirish uchun qilingan sozlamalar (kod o'zgartirilmagan)

1. **`libldap` symlink**: .NET'ning `System.DirectoryServices.Protocols` 8.0 kutubxonasi Linux'da `libldap-2.5.so.0` ni qidiradi,
   Ubuntu 24.04'da esa `libldap.so.2` (2.6) bor. Tuzatish: `ln -s libldap.so.2 /usr/lib/x86_64-linux-gnu/libldap-2.5.so.0` (`liblber` uchun ham).
   Busiz xato: `The type initializer for 'Ldap' threw an exception`.
2. **LDAP sozlamalari** muhit o'zgaruvchilari orqali (`LDAP__LDAPRepresentationsExtractionJobOptions__*`), `start-idserver-ldap.sh` ichida.
3. **Mapping va 2 ta sozlama** admin API orqali (`sync-ldap.sh`). PostgreSQL rejimida mapping va import qilingan xodimlar saqlanadi; qayta ishga tushirish xavfsiz (mapping takrorlanmaydi).

### SimpleIdServer'dagi xatolar va tuzatishlar (`patches/`)

- **Takroriy guruh xatosi** (tuzatildi): bir sahifadagi ikki xodim bitta guruhda bo'lsa, guruh ikki marta saqlanib, EF Core
  `another instance with the same key` (PostgreSQL'da MERGE) xatosini berardi. Endi guruhlar sahifa ichida bir marta qaytadi;
  extract va import'da ham takroriy kalitlar olib tashlanadi. `BatchSize=50`.
- **Delta sinxronlash** (tuzatildi): `modifyTimestamp` operatsion atribut, faqat nomi bilan so'ralganda qaytadi; kod uni so'ramagani
  uchun versiya har safar tasodifiy GUID bo'lardi. Endi so'raladi: o'zgarmagan yozuvlar o'tkaziladi, o'zgarganlari import qilinadi (sinaldi).
- **memberOf** (tuzatildi): AD strategiyasida bir nechta guruhdagi foydalanuvchi butunlay o'tkazib yuborilardi; endi hamma guruhlari o'qiladi.
- **EF Core / linq2db** (tuzatildi): runtime EF Core 9 (Duende storage tortadi), `linq2db.EntityFrameworkCore` esa 8.1 edi:
  relyatsion bazada provisioning `TypeLoadException ... SqlTransparentExpression.Quote` bilan yiqilardi. Paketlar 9.x ga tenglashtirildi.
- **Restartdan keyingi login** (tuzatildi): shifri ochilmaydigan `ReturnUrl` `/master/pwd/CfDJ8...` ga 404 berardi; endi realm sahifasiga.
- **Admin panel tokeni** (tuzatildi): IdServer kalitlari almashsa admin panel 401 olib `/availablerealms` ga tushardi; endi tokenni yangilaydi.
- **A'zolik o'zgarishi** (tuzatildi): a'zolik guruhda saqlangani uchun xodimning `modifyTimestamp`i o'zgarmasdi va u qayta
  import qilinmasdi; olib tashlangan a'zoliklar esa IdServer'da qolib ketardi. Endi versiyaga guruhlar xeshi qo'shiladi va
  manbada yo'q a'zoliklar o'chiriladi (IdServer'da qo'lda yaratilgan guruhlarga tegilmaydi).
- **Jadval bo'yicha sync va deprovisioning** (qo'shildi): `IdentityProvisioningSyncJob` (Hangfire, `IdentityProvisioningSync:Cron`,
  `IdentityProvisioningSync:Definitions`) extract'dan keyin importni o'zi boshlaydi. Import tugagach `UserDeprovisioningService`
  manbadagi to'liq ro'yxatni oladi: yo'q yoki `DisabledUserFilter`ga tushgan xodimni bloklaydi (`UnblockDateTime=9999-12-31` belgisi bilan)
  va faol sessiyalarini bekor qiladi; qayta yoqilganini blokdan chiqaradi. `MaxDeprovisioningPercentage` (20%) chegarasi.
- **Admin panel ruxsati** (tuzatildi): rol talab qilinmagani uchun istalgan autentifikatsiyadan o'tgan xodim admin panelga kirardi
  (panel API'ni o'z client-credentials tokeni bilan chaqiradi). Endi `SIDS-manager/administrator` roli kerak; rolsiz xodim
  "Access denied" sahifasini ko'radi (avvalgi `RedirectLogin` bunda cheksiz aylanishga tushardi).
- **Sinalgan ssenariylar** (2026-10-19, PostgreSQL): mover (guruhdan chiqish/qo'shilish), bloklangan xodim, o'chirilgan xodim,
  20% chegara, qayta yoqish, admin blokiga tegmaslik, bloklangan xodim login qila olmasligi, sessiyalar bekor qilinishi.
- **AD sozlamalari** (sinov AD'da sinalgan, `start-all.sh` dagi `ad` profili): `UseSsl=true`, LDAPS port, `UserIdLDAPAttribute=objectGUID`,
  `GroupIdLDAPAttribute=objectGUID`, `UserObjectClasses=user`, `GroupObjectClasses=group`, `ModificationDateAttribute=whenChanged`,
  `RetrievingStrategies=LOAD_FROM_USER_MEMBEROF_ATTRIBUTE`, `MemberOfAttribute=memberOf`,
  `DisabledUserFilter=(userAccountControl:1.2.840.113556.1.4.803:=2)`, login = `sAMAccountName`, xizmat akkaunti faqat o'qish uchun.

### Sinov AD'da topilgan va tuzatilgan xatolar (2026-10-19)

- **objectGUID noto'g'ri o'qilardi**: ikkilik ID har doim SID deb o'qilardi, .NET esa ba'zi baytlarni satr qilib qaytaradi.
  Endi `objectGUID` → GUID, `objectSid` → `S-1-5-...` (Windows'ga bog'liq `SecurityIdentifier`siz).
- **Ustunlar siljishi**: xodimda mapping atributi bo'lmasa (AD'da `mail` va boshqalar ko'pincha bo'sh), keyingi qiymatlar boshqa
  atributlarga yozilardi (sinovda `bolim` va `lavozim` almashib qoldi). Endi bo'sh atribut o'z o'rnini saqlaydi.
- **Apostrof va unicode**: qiymatlar JSON escape bilan saqlanardi (`bo\u0027lim`, kirill ismlar `\u04..`). Ko'p qiymatli atributlar ham buzilardi.
- **LDAPS bilan login ishlamasdi**: `LDAPAuthenticationService` `UseSsl`ni e'tiborsiz qoldirardi.
- **Bo'sh parol**: LDAP simple bind bo'sh parol bilan "unauthenticated bind" bo'ladi va ko'p serverlar uni qabul qiladi; endi rad etiladi.
- **Qayta ishga olish**: AD'da o'chirilib qayta yaratilgan xodim (yangi `objectGUID`, eski login) kira olmasdi, chunki login eski
  bloklangan yozuvni topardi. Endi o'chirilgan xodimning logini AD kabi bo'shatiladi: `login~removed~sana`.

## Barcha loyihalarning ishga tushish holati (Linux, .NET 10/8, 2026-10-19)

Eski .NET (3.1, 5, 6) loyihalari `DOTNET_ROLL_FORWARD=Major` bilan .NET 10'da ishga tushirildi.

| Loyiha | Natija | Manzil / izoh |
|---|---|---|
| SimpleIdServer (IdServer, Admin, SCIM) | Ishlayapti | 5001, 5002, 5003 (yuqoriga qarang) |
| SCIMReferenceCode | Ishlayapti | http://localhost:6001/scim — token: `GET /scim/token`; user yaratish/qidirish sinaldi |
| AuthPermissions Example1 | Ishlayapti | http://localhost:6002 — kirish: `Super@g1.com` / `Super@g1.com` |
| ADAuth | Ishlayapti | https://127.0.0.1:6003 — `config.json` + sinov sertifikati qo'shildi; AD bo'lmagani uchun 401 qaytaradi |
| SelfService | Ishlayapti | https://localhost:6014 — parol almashtirish sahifasi; AD (`dc01.contoso.local`) yo'q, shuning uchun amal bajarilmaydi |
| CustomApproval | Ishlayapti | http://localhost:6005 — Azure AD B2C sozlamalarisiz faqat UI |
| Novell LDAP | Testlar o'tdi | 231 unit-test o'tdi (net8, net9) |
| SCIM.Client | Testlar o'tdi | 1/1 unit-test |
| resourcemanagement-client | Build bo'ldi | Kutubxona; ishlashi uchun MIM Service kerak |
| SelfService testlari | Build bo'lmadi | Test kodida muallifning xatosi (`Options.Create` nomlar to'qnashuvi) |
| pingcastle | Build bo'lmadi | Build `powershell.exe` talab qiladi, net10.0-windows: Windows'da ishlaydi |
| MIMWAL | Build bo'lmadi | .NET Framework 3.5: faqat Windows + Visual Studio |
| OpenICF.Net | Build bo'lmadi | .NET Framework 4.5.2, eski MSBuild vazifalari: faqat Windows |
| ad-password-protection | Build bo'lmadi | C++ qismi, Visual Studio kerak: faqat Windows |
| ecma2-framework, googleapps-managementagent, miis-autosync | Build bo'lmadi | Lithnet'ning yopiq NuGet manbasi (pkgs.dev.azure.com/lithnet) kerak |

## Java'dagi ochiq IdM'lar (g'oya va tushunchalar uchun)

C# emas, lekin rol modeli, attestatsiya, SoD tushunchalari eng yaxshi shu yerda:
[midPoint](https://github.com/Evolveum/midpoint), [Apache Syncope](https://github.com/apache/syncope).
