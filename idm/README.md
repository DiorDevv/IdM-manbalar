# Bank IdM yadrosi va portali

IdM'ning o'z qismi: xodimlar, tizimlar, akkauntlar, rollar, arizalar va o'zgartirib bo'lmaydigan audit jurnali.
Login (SSO) uchun SimpleIdServer ishlatiladi, qolgan hamma narsa shu yerda.

```
src/Bank.Idm.Domain/          ma'lumot modeli (Identity, Application, Account, Entitlement, Role, AccessRequest, AuditEvent)
src/Bank.Idm.Connectors/      konnektorlar: IConnector, LDAP/AD (faqat o'qish)
src/Bank.Idm.Infrastructure/  EF Core + PostgreSQL (alohida "idm" bazasi), migratsiyalar, AuditLog (xesh zanjiri), Sync/ (solishtirish)
src/Bank.Idm.Api/             ASP.NET Core API, http://localhost:7100
tests/Bank.Idm.Tests/         xunit v3 + Testcontainers (haqiqiy PostgreSQL)
portal/                       React + TypeScript (Vite), http://localhost:5173
```

## Ishga tushirish
`./start-all.sh` hammasini o'zi qiladi: `idm` bazasini yaratadi, SimpleIdServer'da `idm-portal` ilovasi va
`idm` ruxsat doirasini ro'yxatdan o'tkazadi (`bootstrap/register-idm-portal.sh`), API va portalni ishga tushiradi.
Portalga `administrator` bilan kiriladi (parol: `.secrets/idserver.env`).

Qo'lda:
```bash
cd idm && dotnet build && dotnet test          # testlar uchun Docker kerak (yoki IDM_TEST_POSTGRES, pastda)
cd idm/portal && npm ci && npm run dev
```

Docker bo'lmasa, testlar mavjud PostgreSQL'da vaqtinchalik baza yaratadi (superuser kerak), LDAP testlari esa
`sinov-ldap/bank-test-data.ldif` yuklangan serverga ulanadi (ko'rsatilmasa o'tkazib yuboriladi):
```bash
IDM_TEST_POSTGRES="Host=127.0.0.1;Username=postgres;Database=postgres" IDM_TEST_LDAP=ldap://127.0.0.1:389 dotnet test
```

## Konnektorlar (C bosqichi)
Har bir tizim `Idm:Connectors:{kod}` bo'limida. `start-all.sh` sinov AD yoki OpenLDAP uchun to'ldiradi (`LDAP_SOURCE`).
Standart qiymatlar Active Directory uchun, shuning uchun AD'da faqat ulanish va OU'lar yetarli:
```json
"Connectors": {
  "AD": {
    "Name": "Active Directory", "IntervalMinutes": 15, "MaxDeletePercent": 20, "PrivilegedGroups": [ "Domain Admins" ],
    "Ldap": { "Server": "dc1.bank.uz", "Port": 636, "UseSsl": true, "BindDn": "CN=svc-idm,...", "BindPassword": "<sir>",
              "UsersDn": "OU=Xodimlar,DC=bank,DC=uz", "GroupsDn": "OU=Guruhlar,DC=bank,DC=uz" }
  }
}
```
- **O'qish:** sahifalab (AD MaxPageSize), katta guruhlar `member;range=` bilan, `objectGUID` o'zgarmas kalit, bloklanganlar
  `DisabledFilter` bilan. LDAPS sertifikati Linux'da `LDAPTLS_CACERT` orqali tekshiriladi; `libldap.so.2` kerak.
- **Solishtirish:** akkaunt, guruh va a'zolikdagi har bir o'zgarish auditda (birinchi yuklash — bitta umumiy yozuv).
  Yo'qolgan akkaunt "o'chirilgan" deb belgilanadi, guruh esa `deleted_at` bilan saqlanadi (rollar unga bog'langan bo'lishi mumkin).
- **Korrelyatsiya:** `employeeNumber` = xodimning tabel raqami. Tabel raqamisiz akkaunt — "egasiz". HR ulanguncha (D) xodim
  shu tizimdan yaratiladi (`Source = ActiveDirectory`) va ma'lumoti, holati, rahbari (`manager`) yangilanib turadi;
  HR va qo'lda kiritilgan xodimlarga tegilmaydi.
- **Xavfsizlik chegarasi:** bir ishga tushishda `MaxDeletePercent`dan ko'p akkaunt yo'qolsa, hech narsa o'zgarmaydi
  (holat "To'xtatildi"). Administrator portalda tekshirib, tasdiqlab qayta ishga tushiradi (`connector.mass-delete-approved`).
- Bir tizim uchun bir vaqtda bitta sinxronlash; server qayta ishga tushganda tugallanmagan ishga tushish "Xato" bo'ladi.

## HR manbai (D bosqichi)
HR — xodimlar haqidagi yagona haqiqat manbai. Hozircha eksport fayli (CSV, UTF-8, `;` yoki `,`), namuna: `sinov-hr/xodimlar.csv`.
```json
"HR": { "Type": "hr-csv", "Name": "Kadrlar tizimi", "IntervalMinutes": 15, "MaxDeletePercent": 20, "TerminateMissing": false,
        "Csv": { "Path": "/srv/hr/xodimlar.csv", "Columns": { "EmployeeNumber": "Tabel", "LastName": "Familiya", "FirstName": "Ism" } } }
```
- **Ustunlar:** `employeeNumber`, `lastName`, `firstName` majburiy; `middleName`, `email`, `department`, `jobTitle`,
  `managerEmployeeNumber`, `hireDate`, `terminationDate` (`dd.MM.yyyy` yoki `yyyy-MM-dd`), `status` (faol / ta'til / ishdan ketgan).
- **Qat'iylik:** majburiy ustun yo'q, tabel raqami takrorlangan, fayl UTF-8 emas yoki buzilgan bo'lsa — butun import rad etiladi.
  Noto'g'ri qator o'tkazib yuboriladi (ogohlantirishda faqat qator va tabel raqami, shaxsiy ma'lumot yo'q) va "yo'qolgan" hisoblanmaydi.
- **Holat:** `terminationDate` kelgan (Toshkent vaqti bo'yicha, `TimeZone`) yoki status "ishdan ketgan" — Terminated; `hireDate` kelmagan — Pending.
- **Egalik:** HR'dagi xodim HR manbali bo'ladi (AD'dan yaratilgan bo'lsa ham). HR ulanganda AD xodim yaratmaydi
  (`CreateIdentities=false`): HR'da yo'q akkaunt — egasiz.
- **JML hodisalari** (`identity_events`): ishga kirish, lavozim/bo'lim/rahbar o'zgarishi, ishdan ketish, qayta ishga olish, to'xtatish.
  Birinchi yuklash hodisa yozmaydi. G bosqichida shu hodisalar AD'da amallarni boshlaydi; hozir tizimlarga tegilmaydi.
- **HR faylida yo'q xodim:** standart — faqat ogohlantirish. `TerminateMissing=true` — ishdan ketgan deb belgilanadi
  (eksport to'liq ekaniga ishonch bo'lgandagina). Ko'p xodim birdan ketsa (`MaxDeletePercent`) — import to'xtatiladi.
- **Hisobotlar:** ishdan ketgan, lekin akkaunti faol; egasiz akkauntlar; akkauntsiz xodimlar; HR'da yo'q xodimlar.

## Rollar va SoD (E bosqichi)
- **Rol** — biznes roli: tizimlardagi ruxsatlar (AD guruhlari) to'plami. Kod, nom, xavf darajasi, ariza orqali so'rash mumkinligi.
- **Avtomatik ("tug'ma") qoida:** bo'lim va/yoki lavozim (bo'sh — "har qanday", ikkalasi bo'sh — taqiqlangan). Qoidaga mos
  ishlayotgan (va ishga kirishi kutilayotgan) xodimga rol beriladi, mos kelmay qolsa yoki ishdan ketsa — olib tashlanadi.
  Qayta hisoblash har muvaffaqiyatli sinxronlashdan keyin avtomatik va "Qayta hisoblash" tugmasi bilan; qoida qo'shilganda avval
  "N ta xodimga mos" ko'rinadi. Faqat avtomatik tayinlashlarga tegadi; olib tashlangan rol o'chirilmaydi (`valid_to`), har o'zgarish auditda.
  Parallel hisoblashdan advisory lock va `identity_roles` dagi noyob indeks himoya qiladi.
- **"Bo'lishi kerak / haqiqatda bor":** rollardan kelgan ruxsatlar va tizimdan o'qilgan a'zolik solishtiriladi: *Bor*, *Yetishmaydi*
  (G bosqichida beriladi), *Ortiqcha* (rol bilan asoslanmagan — H bosqichida attestatsiya). Xodim kartochkasi va "Ruxsatlar farqi" hisoboti.
- **SoD:** qoida — ikki tomon ruxsatlari (A va B); xodimda haqiqatda ikkala tomondan ruxsat bo'lsa — buzilish. Rolning o'zi
  qoidani buzsa, rol sahifasida ogohlantirish. Buzilishlar bosh sahifa, menyu va xodim kartochkasida ko'rinadi.
- Ishlatilgan yoki arizada qatnashgan rolni o'chirib bo'lmaydi (tarix uchun).

## Xavfsizlik
- **Kirish:** SSO (OIDC avtorizatsiya kodi + PKCE), portal ochiq ilova, maxfiy kalit yo'q. Token `sessionStorage`da.
- **API:** JWT imzosi, issuer, audience (`idm-portal`) va `idm` ruxsat doirasi tekshiriladi.
- **Vakolatlar** (`idm.admin`, `idm.auditor`, `idm.hr`) IdM bazasida (`portal_role_assignments`). SSO tokenidagi
  rollarga ishonilmaydi. Birinchi administrator: `Idm:BootstrapAdmins`. Oddiy xodim portalga kiradi, lekin ma'lumot ko'rmaydi.
- **Audit:** har bir o'zgarish bilan bitta tranzaksiyada yoziladi. Baza triggeri `UPDATE/DELETE/TRUNCATE`ni taqiqlaydi;
  har bir yozuv oldingisining SHA-256 xeshini saqlaydi (`GET /api/audit/verify`), triggerni chetlab o'tgan o'zgarish ham aniqlanadi.
  Ishlab chiqarishda ilova jadval egasi bo'lmagan, faqat `INSERT/SELECT` huquqli rol bilan ulanishi kerak (I bosqichi).
- Javoblarda `nosniff`, `X-Frame-Options: DENY`, `no-store`, `X-Correlation-Id`.

## API
| Manzil | Kim |
|---|---|
| `GET /health` | hamma |
| `GET /api/me` | har qanday xodim |
| `GET /api/dashboard`, `/api/identities`, `/api/identities/{id}`, `/api/identities/{id}/history`, `/api/audit`, `/api/audit/verify` | admin, auditor |
| `GET /api/applications`, `/api/applications/{kod}`, `.../accounts?orphan=true`, `.../entitlements`, `.../runs` | admin, auditor |
| `GET /api/reports/correlation`, `/api/reports/{leavers-with-active-accounts, orphan-accounts, identities-without-accounts, not-in-hr}`, `/api/events?kind=` | admin, auditor |
| `GET /api/roles`, `/api/roles/{id}`, `/api/identities/{id}/access`, `/api/reports/access-drift`, `/api/sod/rules`, `/api/sod/violations`, `/api/entitlements`, `/api/identity-attributes` | admin, auditor |
| `POST /api/identities`, `POST /api/applications/{kod}/sync[?allowMassDelete=true]` | admin |
| `POST/PUT/DELETE /api/roles…` (rol, ruxsatlar, qoidalar), `POST /api/roles/recalculate`, `POST/DELETE /api/sod/rules…` | admin |

Migratsiya qo'shish: `cd idm && dotnet ef migrations add <Nom> --project src/Bank.Idm.Infrastructure --startup-project src/Bank.Idm.Infrastructure --output-dir Migrations`
