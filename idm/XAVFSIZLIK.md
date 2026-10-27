# Bank IdM: xavfsizlik hujjati

Bankning axborot xavfsizligi bo'limi, pentest jamoasi va ekspluatatsiya uchun. Har bir nazorat — qayerda amalga oshirilgan va qaysi
avtomatik test uni tekshiradi. Sozlamalar tafsiloti: [README.md](README.md).

## 1. Tizim va chegaralar

```
 Xodim / rahbar / admin ── brauzer ──► Portal (React, statik) ──► IdM API (.NET, ≥2 nusxa) ──► PostgreSQL (VerifyFull TLS)
                               │                                     │  ├─► AD / LDAP (LDAPS, o'qish va alohida yozish akkaunti)
                               └──► SSO (SimpleIdServer, OIDC) ◄─────┘  ├─► HR eksport fayli (o'qish)
                                                                        ├─► SIEM (syslog TLS)
                                                                        └─◄ Prometheus (/metrics, token)
```

| Aktiv | Nega muhim |
|---|---|
| AD'ga yozish huquqi (yozish xizmat akkaunti) | Butun bank xodimlarini bloklash, imtiyozli guruhga qo'shish mumkin |
| IdM administratori vakolati | Rollar, SoD, tizimga yozishni tasdiqlash |
| Audit jurnali | Regulyator va tergov uchun yagona dalil |
| Xodimlar va ruxsatlar ma'lumoti | Shaxsiy ma'lumot va hujum xaritasi (kimda Domain Admins bor) |

## 2. Tahdidlar va nazoratlar

| Tahdid | Nazorat | Tekshiruv (test) |
|---|---|---|
| Soxta yoki boshqa ilova token'i | JWT imzo, issuer, audience `idm-portal`, `idm` scope majburiy | `ApiTests` |
| SSO token'idagi soxta `idm.admin` roli | Vakolat faqat IdM bazasidan, SSO'dagi `idm.*` rollar o'chiriladi | `ApiTests` (X-Test-Forged-Role) |
| Admin paroli o'g'irlangan | Admin/auditor vakolati faqat ikkinchi omil bilan (`AdminMfa`, `amr`/`acr`); portal step-up | `HardeningApiTests` |
| O'z ruxsatini o'zi tasdiqlash | Ariza va attestatsiyada o'zini tasdiqlash taqiqi; tekshiruvchi — rahbar yoki administrator navbati | `RequestTests`, `ReviewTests` |
| Bir vaqtda ikki qaror (poyga) | PostgreSQL `xmin` optimistik qulfi, advisory lock'lar | `RequestTests`, `ProvisioningTests` |
| Noto'g'ri HR fayli / OU — ommaviy bloklash | Sinxronlashda `MaxDeletePercent`, tizimga yozishda `MaxAutoOperations` (oshsa — tasdiqqa), standart rejim `Off`/`Approval` | `HrSyncTests`, `ConnectorSyncTests`, `ProvisioningTests` |
| Eskirgan ma'lumot bilan AD'ga yozish | Oxirgi sinxronlash muvaffaqiyatsiz yoki ketayotgan bo'lsa reja tuzilmaydi; bajarishdan oldin keraksiz amal bekor | `ProvisioningTests` |
| Administrator akkauntini avtomatik bloklash | `ProtectedLogins`, xizmat akkauntlariga tegilmaydi | `ProvisioningTests` |
| Boshqa OU'dagi obyektga yozish, LDAP in'eksiyasi | Faqat `UsersDn`/`GroupsDn` ichida, o'zgarmas ID bo'yicha; filtr va DN qiymatlari RFC 4515/4514 bo'yicha ekranlanadi | `LdapProvisionerUnitTests`, `LdapProvisionerLiveTests` |
| IdM qo'shmagan ruxsatni noto'g'ri rol tufayli olib tashlash | Rol olinsa faqat IdM o'zi qo'shgan a'zolik olinadi; qolgani — attestatsiyada rahbar qaroriga | `ProvisioningTests` |
| Audit yozuvini o'zgartirish yoki o'chirish | Baza triggeri; SHA-256 zanjir (`/api/audit/verify`, `verify-audit`); `idm_app` rolida UPDATE/DELETE/TRUNCATE yo'q; SIEM'ga nusxa (xesh bilan) | `AuditLogTests`, `SiemTests`, `deploy/db-roles.sql` |
| Sirlar sozlama faylida yoki repoda | Production'da `appsettings*.json`dagi sir — server ishga tushmaydi; sirlar katalogi / muhit | `ProductionGuardTests`, `HardeningApiTests` |
| Shifrlanmagan ulanish (baza, LDAP, SSO, SIEM) | Production'da VerifyFull, LDAPS, HTTPS, `tcp-tls` majburiy — aks holda ishga tushmaydi | `ProductionGuardTests`, `SiemTests` (begona CA rad etiladi) |
| Proksi sarlavhalarini soxtalashtirish (IP almashtirish) | `X-Forwarded-*` faqat `TrustedProxies`dan | `ProductionGuardTests` |
| Excel/CSV formula in'eksiyasi | Excel'da barcha qiymatlar matn (inlineStr), formula yo'q | `ReviewTests` |
| XSS | Portal: React, `dangerouslySetInnerHTML` yo'q, server matni faqat matn; API: `nosniff`, `no-store`, `X-Frame-Options: DENY` | kod ko'rib chiqish |
| Begona xodim ma'lumotini ko'rish | Arizaning borligi ham oshkor qilinmaydi (404); hisobot va eksport — admin/auditor; har eksport auditda | `RequestTests`, `ReviewTests` |
| Server nusxasi qulashi, ish yarim qolishi | Yetakchi tanlash (advisory lock), to'xtagan nusxa ishlarini yopish, idempotent amallar | `ClusterTests`, jonli sinov (2 s) |
| Zaxira nusxa yaroqsiz bo'lib chiqishi | `deploy/restore-verify.sh`: nazorat summasi, audit belgisi, butun zanjir | qo'lda / taymer |
| Zaif bog'liqliklar | CI: `dotnet list package --vulnerable`, `npm audit --audit-level=high` | CI |

## 3. Pentest uchun tekshirish ro'yxati

Sinov muhiti: `start-all.sh` (OpenLDAP yoki sinov AD), portal `http://localhost:5173`, API `http://localhost:7100`.
Rollar: oddiy xodim, rahbar (xodimlari bor), administrator, auditor — har biri bilan alohida.

- [ ] **Vakolat (IDOR):** boshqa xodimning arizasi, attestatsiya yozuvi, tizimga yozish amali, hisobot — ID almashtirib (`/api/requests/{id}`,
  `/api/reviews/decide`, `/api/provisioning/operations/approve`, `/api/reports/export/*`).
- [ ] **Vertikal:** oddiy xodim bilan `/api/identities`, `/api/provisioning/*`, `/api/reviews/campaigns` (403 kutiladi).
- [ ] **MFA chetlab o'tish:** `AdminMfa` yoqilganda faqat parol bilan olingan token'da admin amallari (403 kutiladi); refresh token'dan keyin.
- [ ] **Token:** boshqa audience/klient token'i, scope'siz token, muddati o'tgan, imzosi buzilgan, `alg=none`.
- [ ] **LDAP in'eksiyasi:** HR faylidagi ism/familiya (`*`, `)(`, `\`, `,`, `=`), login yaratish; AD'da natija (`CreateDn` tashqarisiga yozilmasin).
- [ ] **Ommaviy amal:** 30% xodim "ishdan ketgan" HR fayli — sinxronlash to'xtashi; `Automatic` rejimda chegaradan ko'p amal — tasdiqqa to'xtashi.
- [ ] **Poyga:** bir arizani ikki tasdiqlovchi bir vaqtda; bir amalni ikki marta tasdiqlash; ikki nusxada bir tizimni bir vaqtda sinxronlash.
- [ ] **Audit:** bazada `UPDATE audit_events` (trigger rad etadi); trigger o'chirilib qilingan o'zgarish `verify-audit`da ko'rinishi.
- [ ] **Eksport:** Excel'dagi `=HYPERLINK(...)` qiymati formula bo'lmasligi; katta hisobot (DoS): chop etishda 413.
- [ ] **Sarlavhalar va TLS:** HSTS, `nosniff`, kesh yo'qligi; LDAPS/PostgreSQL/SIEM sertifikat tekshiruvi (soxta sertifikat bilan MITM).
- [ ] **Metrikalar:** `/metrics` tokensiz va noto'g'ri token bilan (401/404), javobda shaxsiy ma'lumot yo'qligi.
- [ ] **Portal:** XSS (xodim ismi, rol tavsifi, audit tafsiloti orqali), CSP (proksida), token `sessionStorage`da, chiqishdan keyin qayta foydalanish.

## 4. Ishlab chiqarishga chiqarish ro'yxati

- [ ] PostgreSQL: `deploy/db-roles.sql`; API `idm_app` bilan, migratsiyalar `idm_owner` bilan alohida (`MigrateOnStartup=false`); `SSL Mode=VerifyFull`.
- [ ] Sirlar: Vault Agent / Kubernetes secrets → `Idm__SecretsDirectory`; repoda va `appsettings*.json`da sir yo'q (server tekshiradi).
- [ ] TLS: API oldida bank proksisi (TLS 1.2+, bank sertifikati), `Idm:TrustedProxies`; CSP portal uchun proksida.
- [ ] SSO: `patches/simpleidserver-access-token-acr.patch` qo'llangan; ACR (parol + OTP) yaratilgan; `Idm:Sso:AdminMfa:Required=true`, `StepUpAcr`.
- [ ] AD: o'qish va yozish uchun alohida xizmat akkauntlari; yozish akkauntiga faqat `UsersDn`/`GroupsDn`da kerakli huquqlar (delegatsiya);
  imtiyozli guruhlar (Domain Admins) uchun alohida qaror: IdM boshqaradimi yoki `ProtectedLogins`/qo'lda.
- [ ] Tizimga yozish: avval bir necha hafta `Approval` (dry-run) — natijani axborot xavfsizligi bilan ko'rib chiqish; keyin `Automatic` past `MaxAutoOperations` bilan.
- [ ] SIEM: `tcp-tls`, bank CA; SIEM'da `bank-idm` manbasi uchun qoidalar (`provisioning.held`, `report.exported`, `review.revoked`, `portal-role.granted`).
- [ ] Monitoring: `/metrics` (token), README'dagi ogohlantirish qoidalari; `/health` yuk taqsimlagichda.
- [ ] HA: kamida 2 nusxa, `idm_leader` yig'indisi = 1 ogohlantirishi.
- [ ] Zaxira: `deploy/backup.sh` (shifrlangan, boshqa saqlovchiga), `deploy/restore-verify.sh` haftada; tiklash vaqti (RTO) va yo'qotish oralig'i (RPO) kelishilgan.
- [ ] `BootstrapAdmins` birinchi ishga tushishdan keyin olib tashlangan; admin va auditor vakolatlari ro'yxati axborot xavfsizligi bilan tasdiqlangan.

## 5. Ma'lum cheklovlar va ochiq xavflar

- **TCP syslog ilova darajasida tasdiqlanmaydi** (RELP yo'q): ulanish uzilganda oxirgi to'plamning bir qismi takror yuborilishi mumkin
  (yo'qolmaydi — checkpoint yuborishdan keyin yangilanadi); SIEM `id` bo'yicha takrorni tashlashi kerak.
- **Akkaunt yaratish** bloklangan va parolsiz: parolni helpdesk beradi — jarayon bank tomonida tasdiqlanishi kerak.
- **Yakunlangan attestatsiyada ko'rib chiqilmagan ruxsatlar** avtomatik olinmaydi (siyosat bank qarori bilan qo'shiladi).
- **SimpleIdServer** — alohida mahsulot: o'z qabul testlarida (`SimpleIdServer.IdServer.Host.Acceptance.Tests`, 316 ta) bizning
  patch'larimizdan oldin ham 18 ta yiqilish bor (2026-10-27 holati; access token patch'i bu sonni o'zgartirmaydi). SSO'ni bank
  standartlariga moslash alohida baholanadi.
- **Portal CSP** API'da emas, statik fayllarni beradigan proksida o'rnatiladi.
