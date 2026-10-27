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
  Birinchi yuklash hodisa yozmaydi. Xodim holati (ishdan ketgan, to'xtatilgan) tizimga yozish rejasiga kiradi (G bosqichi).
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
  (tizimga yozish bilan beriladi), *Ortiqcha* (rol bilan asoslanmagan — attestatsiyada rahbar ko'rib chiqadi). Xodim kartochkasi va "Ruxsatlar farqi" hisoboti.
- **SoD:** qoida — ikki tomon ruxsatlari (A va B); xodimda haqiqatda ikkala tomondan ruxsat bo'lsa — buzilish. Rolning o'zi
  qoidani buzsa, rol sahifasida ogohlantirish. Buzilishlar bosh sahifa, menyu va xodim kartochkasida ko'rinadi.
- Ishlatilgan yoki arizada qatnashgan rolni o'chirib bo'lmaydi (tarix uchun).

## Arizalar (F bosqichi)
- Portalga kirgan har bir xodim (AD akkaunti orqali xodim yozuviga bog'langan) o'zi yoki **bevosita xodimlari** uchun
  `IsRequestable` rolni so'raydi: asos (10–2000 belgi), ixtiyoriy muddat (≤ 1 yil, tugaganda rol o'zi tugaydi).
- **Zanjir:** rahbar → rol egasi (rahbar bilan bir xil bo'lsa — bitta bosqich) → SoD ziddiyati bo'lsa axborot xavfsizligi.
  Tasdiqlovchi belgilanmagan bosqich IdM administratorlari navbatiga tushadi. Rahbar o'zi so'rasa, rahbar bosqichi avtomatik.
  Yuborishdan oldin "oldindan ko'rish" zanjir va SoD ziddiyatlarini ko'rsatadi.
- **Qoidalar:** o'z arizasini (so'ragan yoki kim uchun so'ralgan) hech kim tasdiqlay olmaydi; faqat navbatdagi bosqich egasi qaror
  qiladi; rad etishda sabab majburiy; takror ariza va mavjud rolga ariza rad etiladi; ikki kishi bir vaqtda qaror qilsa —
  ikkinchisi 409 (PostgreSQL `xmin` optimistik qulfi). Begona xodim arizaning borligini ham ko'rmaydi (404).
- Oxirgi tasdiqdan keyin rol beriladi (`Source = Request`) — "bo'lishi kerak"; rolning guruhlari xodimda haqiqatda paydo bo'lgach
  (IdM yozgan yoki administrator qo'lda bergan) ariza "Bajarildi" holatiga o'tadi (`request.fulfilled`).
- **Ishdan ketganda** barcha rollar (ariza va qo'lda berilganlar ham) tugaydi, kutilayotgan arizalar bekor qilinadi.

## Tizimga yozish (G bosqichi)
Rejalashtiruvchi "bo'lishi kerak" (rollar, xodim holati) va tizimdan o'qilgan holatni solishtirib amallar navbatini tuzadi
(`provisioning_operations`), ishlovchi ularni tizimda bajaradi. Standart — o'chiq; har tizim uchun alohida yoqiladi:
```json
"AD": { "Provisioning": { "Mode": "Approval", "CreateAccounts": false, "MaxAutoOperations": 20, "ProtectedLogins": [ "administrator" ],
                          "DisableSuspended": true, "MaxAttempts": 5 },
        "Ldap": { "WriteBindDn": "CN=svc-idm-write,...", "WriteBindPassword": "<sir>", "CreateDn": "OU=Yangi,OU=Xodimlar,DC=bank,DC=uz",
                  "UpnSuffix": "bank.uz" } }
```
- **Rejimlar:** `Off`; `Approval` — dry-run: amallar "Tasdiq kutmoqda", administrator portalda (*Tizimga yozish*) tanlab tasdiqlaydi;
  `Automatic` — darhol navbatga, lekin bitta rejada `MaxAutoOperations`dan ko'p yangi amal chiqsa hammasi tasdiqqa to'xtatiladi
  (`provisioning.held`) — noto'g'ri HR fayli yoki rol butun bankni bloklab yubormasligi uchun.
- **Nima qilinadi:** ishdan ketgan xodim — barcha akkauntlari bloklanadi va barcha guruhlardan chiqariladi; to'xtatilgan — bloklanadi;
  rol bo'yicha kerak guruh — shaxsiy akkauntga qo'shiladi; akkaunt yo'q bo'lsa (`CreateAccounts`) — **bloklangan va parolsiz** yaratiladi
  (login `i.familiya`, band bo'lsa raqam bilan; parolni helpdesk beradi, IdM'da parol hech qachon bo'lmaydi); rol olib tashlansa —
  **faqat IdM o'zi qo'shgan** a'zolik olinadi. Qo'lda berilgan ortiqcha ruxsatlarga tegilmaydi (H bosqichida attestatsiya).
- **Tegilmaydi:** xizmat akkauntlari (`Kind = Service`) va `ProtectedLogins`. Yozish faqat `UsersDn`/`GroupsDn` ichida, obyektlar
  o'zgarmas identifikator (`objectGUID`/`entryUUID`) bo'yicha topiladi. Alohida yozish akkaunti (`WriteBindDn`) — eng kam huquq.
- **Ishonchlilik:** oxirgi sinxronlash muvaffaqiyatsiz bo'lsa reja tuzilmaydi; keraksiz bo'lib qolgan amal bekor qilinadi (`Obsolete`);
  amallar idempotent (allaqachon a'zo / bloklangan — xato emas); yaratishdan oldin tabel raqami bo'yicha qidiriladi (javobi yo'qolgan
  urinish takror akkaunt yaratmaydi). Vaqtinchalik xato (tarmoq, server band) — 1, 5, 15, 60 daqiqadan keyin qayta urinish; doimiy
  xato yoki urinishlar tugashi — "Xato", administrator qayta ishga tushiradi yoki yopadi. Tartib: avval bloklash va chiqarish.
  Reja, tasdiq va amalni olish tizim bo'yicha advisory lock ostida, sinxronlash bilan bir vaqtda emas; ikki ishlovchi bitta amalni
  bajarmaydi (`xmin`). Server bajarish paytida to'xtasa, amal navbatga qaytadi.
- **Audit:** reja, har bir tasdiq (kim), har bir bajarilgan va xato amal — akkaunt obyekti bilan, xodim tarixida ko'rinadi.
- Aylanish oralig'i: `Idm:ProvisioningIntervalSeconds` (standart 30, 0 — o'chiq). OpenLDAP uchun: `CreateObjectClasses: ["inetOrgPerson"]`,
  `RdnAttribute: "uid"`, `DisableAttribute: "employeeType"` (`DisabledFilter` bilan mos).

## Attestatsiya va hisobotlar (H bosqichi)
- **Kampaniya** (administrator boshlaydi, portal: *Attestatsiya → Kampaniyalar*): qamrov — bo'lim, tizim, faqat imtiyozli; muddat ≤ 180 kun.
  Boshlashdan oldin oldindan ko'rish: nechta ruxsat, xodim, rahbar va administratorlar navbatiga tushadigan yozuv.
  Ishga tushganda yozuvlar ko'chiriladi (snapshot) — natija keyin ham o'zgarmaydi.
- **Nima ko'rib chiqiladi:** ishlayotgan va to'xtatilgan xodimlarning ariza yoki qo'lda berilgan **rollari** va hech qaysi rol bilan
  asoslanmagan **tizim guruhlari**. Avtomatik (tug'ma) rollar ko'rib chiqilmaydi: ular qoida bilan beriladi va olinadi. Ishdan ketganlar —
  tizimga yozish bilan bloklanadi.
- **Kim:** bevosita rahbar; rahbar yo'q, ishdan ketgan yoki xodimning o'zi bo'lsa — IdM administratorlari. O'z ruxsatini hech kim
  tasdiqlay olmaydi; qaror yakuniy (ikkinchi qaror — 409); bir nechta yozuv bo'yicha qaror — hammasi yoki hech biri.
- **"Olib tashlash":** rol darhol tugaydi (uning IdM qo'shgan guruhlari tizimdan olinadi), asossiz guruh tizimga yozish rejasiga
  tushadi (`review-revoked`, rejim — tizimning `Provisioning:Mode`). Guruh tizimdan haqiqatda yo'qolgach yozuv "tizimdan olindi"
  (`review.remediated`); yozish o'chiq tizimda administrator qo'lda oladi, sinxronlash tasdiqlaydi.
- Yakunlashda ko'rib chiqilmagan yozuvlar shundayligicha qoladi va hisobotda "ko'rib chiqilmagan" deb ko'rinadi.
- **Hisobotlar** (`/api/reports/export/{key}`, admin va auditor): `access` (kim nimaga kirish huquqiga ega va asosi), `privileged`
  (imtiyozli akkauntlar va egalari, xavf belgisi), `sod`, `orphans`, `leavers`, `campaign` (`campaignId`). `format=xlsx` — Excel
  (tashqi kutubxonasiz; barcha qiymatlar matn, "=" bilan boshlangani formula bo'lmaydi), aks holda JSON — portalning chop etish
  ko'rinishi (brauzerda "PDF sifatida saqlash", A4 albom). Har bir yuklab olish auditda (`report.exported`).

## Bir nechta server nusxasi (HA)
- Bir nechta IdM API nusxasi bitta PostgreSQL bilan ishlaydi (yuk taqsimlagich orqasida). Har nusxa `cluster_nodes`da yurak urishini
  yangilaydi (`Idm:Cluster:HeartbeatSeconds`, standart 10). **Yetakchi** — PostgreSQL session advisory lock egasi: jadval bo'yicha
  sinxronlash va tizimga yozish faqat unda. Yetakchi to'xtasa yoki bazaga ulanishi uzilsa, lock bo'shaydi va keyingi yurak urishida
  boshqa nusxa yetakchi bo'ladi (sinovda: 2 soniya).
- Bir tizimda bir vaqtda bitta sinxronlash — barcha nusxalar uchun baza indeksi (`ix_connector_runs_one_running`) kafolatlaydi.
- Yurak urishi `DeadAfterSeconds` (45) davomida bo'lmagan nusxaning tugallanmagan sinxronlashlarini yetakchi yopadi; yetakchi
  bo'lganda "bajarilmoqda"da qolgan yozish amallari navbatga qaytadi (amallar idempotent). To'g'ri to'xtagan nusxa o'zini ro'yxatdan chiqaradi.
- Qo'lda rejalash, tasdiqlash va API so'rovlari istalgan nusxada: tizim bo'yicha advisory lock va `xmin` qulflari bilan.
- `/health` — baza aloqasi, nusxa identifikatori va yetakchimi (yuk taqsimlagich va monitoring uchun).

## SIEM va monitoring
- **SIEM:** `Idm:Siem` — audit jurnali syslog RFC 5424 ko'rinishida, TCP + TLS (RFC 5425, port 6514), RFC 6587 octet-counting ramka.
  Xabar — JSON: kim, nima, qachon, obyekt, tafsilot, IP, korrelyatsiya, `hash` va `previousHash` (SIEM tomonda zanjirni tekshirish mumkin).
  ```json
  "Siem": { "Enabled": true, "Host": "siem.bank.uz", "Port": 6514, "CaCertificatePath": "/etc/idm/bank-ca.pem" }
  ```
  `CaCertificatePath` berilsa, SIEM sertifikati faqat shu (bankning ichki) CA'ga tekshiriladi; host nomi ham tekshiriladi.
  Yetkazish — kamida bir marta: to'plam yuborilgach `siem_checkpoints` yangilanadi, uzilishda shu joydan davom etadi (takrorni SIEM
  `id` bo'yicha tashlaydi). Faqat yetakchi nusxa yuboradi. Ishlab chiqarishda shifrlanmagan `tcp` taqiqlangan, SIEM o'chiq — ogohlantirish.
- **Metrikalar:** `GET /metrics` (Prometheus matn formati), `Authorization: Bearer <Idm:MetricsToken>` (≥ 32 belgi, sirlar orqali;
  bo'sh — endpoint yo'q). `idm_up`, `idm_leader`, `idm_cluster_nodes`, `idm_connector_last_success_age_seconds`,
  `idm_connector_last_run_failed`, `idm_provisioning_operations{status}`, `idm_access_requests_pending`, `idm_review_items_pending`,
  `idm_leavers_with_active_accounts`, `idm_orphan_accounts`, `idm_audit_last_event_id`, `idm_siem_lag_events`,
  `idm_siem_last_success_age_seconds`. Shaxsiy ma'lumot yo'q.
- Tavsiya etiladigan ogohlantirishlar: `idm_leader` yig'indisi 1 emas; sinxronlash 2 × interval'dan ko'p bo'lmagan;
  `idm_provisioning_operations{status="Failed"} > 0`; `idm_leavers_with_active_accounts > 0` (1 soatdan ko'p); SIEM kechikishi > 5 daqiqa.

## Xavfsizlik
- **Kirish:** SSO (OIDC avtorizatsiya kodi + PKCE), portal ochiq ilova, maxfiy kalit yo'q. Token `sessionStorage`da.
- **API:** JWT imzosi, issuer, audience (`idm-portal`) va `idm` ruxsat doirasi tekshiriladi.
- **Vakolatlar** (`idm.admin`, `idm.auditor`, `idm.hr`) IdM bazasida (`portal_role_assignments`). SSO tokenidagi
  rollarga ishonilmaydi. Birinchi administrator: `Idm:BootstrapAdmins`. Oddiy xodim portalga kiradi, lekin ma'lumot ko'rmaydi.
- **Audit:** har bir o'zgarish bilan bitta tranzaksiyada yoziladi. Baza triggeri `UPDATE/DELETE/TRUNCATE`ni taqiqlaydi;
  har bir yozuv oldingisining SHA-256 xeshini saqlaydi (`GET /api/audit/verify`), triggerni chetlab o'tgan o'zgarish ham aniqlanadi.
  Ishlab chiqarishda ilova jadval egasi bo'lmagan, faqat `INSERT/SELECT` huquqli rol bilan ulanishi kerak (I bosqichi).
- Javoblarda `nosniff`, `X-Frame-Options: DENY`, `no-store`, `X-Correlation-Id`; Development'dan boshqa muhitda HSTS.
- **Ikki bosqichli kirish (administrator, auditor):** `Idm:Sso:AdminMfa:Required=true` — token'da ikkinchi omil (`amr`: otp, hwk,
  webauthn...) yoki `AcceptedAcr`/`StepUpAcr` darajasi bo'lmasa, vakolat berilmaydi (foydalanuvchi oddiy xodim sifatida ishlaydi,
  portal "Ikki bosqichli kirish" tugmasini ko'rsatadi va SSO'ga `acr_values=<StepUpAcr>` bilan qayta yuboradi).
  SSO'da: parol + OTP ish oqimi bilan ACR yarating (masalan `idm-mfa`) va `StepUpAcr`ga yozing. SimpleIdServer access token'ga
  `acr`/`amr`ni `patches/simpleidserver-access-token-acr.patch` bilan yozadi (asl holatda faqat id_token'da).
- **Ishlab chiqarish sozlamasi nazorati** (Development va Testing'dan boshqa har qanday muhit): quyidagilardan biri bo'lsa server
  ishga tushmaydi — SSO manzili HTTPS emas yoki sertifikat tekshirilmaydi; PostgreSQL `SSL Mode` VerifyFull/VerifyCA emas;
  LDAP konnektori LDAPS emas; parol/sir `appsettings*.json` faylida; HTTP ochiq, lekin `Idm:TrustedProxies` (TLS'ni tugatuvchi
  proksi) berilmagan. Ogohlantirish: `MigrateOnStartup`, `BootstrapAdmins`, MFA o'chiq, avtomatik yozish alohida yozish akkauntisiz.
- **Sirlar:** muhit o'zgaruvchilari yoki sirlar katalogi — `Idm__SecretsDirectory=/run/secrets/idm` (Vault Agent, Kubernetes yoki
  Docker secrets): har fayl bitta kalit, nomi kalitning o'zi (`ConnectionStrings__Idm`, `Idm__Connectors__AD__Ldap__BindPassword`).
- **Proksi orqasida:** `Idm:TrustedProxies` — faqat shu manzillardan `X-Forwarded-For/Proto` qabul qilinadi (auditda mijozning
  haqiqiy IP manzili).

## API
| Manzil | Kim |
|---|---|
| `GET /health` | hamma |
| `GET /api/me` | har qanday xodim |
| `GET /api/dashboard`, `/api/identities`, `/api/identities/{id}`, `/api/identities/{id}/history`, `/api/audit`, `/api/audit/verify` | admin, auditor |
| `GET /api/applications`, `/api/applications/{kod}`, `.../accounts?orphan=true`, `.../entitlements`, `.../runs` | admin, auditor |
| `GET /api/reports/correlation`, `/api/reports/{leavers-with-active-accounts, orphan-accounts, identities-without-accounts, not-in-hr}`, `/api/events?kind=` | admin, auditor |
| `GET /api/roles`, `/api/roles/{id}`, `/api/identities/{id}/access`, `/api/reports/access-drift`, `/api/sod/rules`, `/api/sod/violations`, `/api/entitlements`, `/api/identity-attributes` | admin, auditor |
| `GET /api/catalog/roles`, `/api/me/team`, `POST /api/requests/preview`, `POST /api/requests`, `GET /api/requests?scope=mine\|inbox`, `GET /api/requests/{id}`, `POST /api/requests/{id}/decision`, `POST /api/requests/{id}/cancel` | har qanday xodim (huquq xizmatda tekshiriladi) |
| `GET /api/requests?scope=all` | admin, auditor |
| `POST /api/identities`, `POST /api/applications/{kod}/sync[?allowMassDelete=true]` | admin |
| `POST/PUT/DELETE /api/roles…` (rol, ruxsatlar, qoidalar), `POST /api/roles/recalculate`, `POST/DELETE /api/sod/rules…` | admin |

Migratsiya qo'shish: `cd idm && dotnet ef migrations add <Nom> --project src/Bank.Idm.Infrastructure --startup-project src/Bank.Idm.Infrastructure --output-dir Migrations`
