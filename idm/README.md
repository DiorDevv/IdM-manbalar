# Bank IdM yadrosi va portali

IdM'ning o'z qismi: xodimlar, tizimlar, akkauntlar, rollar, arizalar va o'zgartirib bo'lmaydigan audit jurnali.
Login (SSO) uchun SimpleIdServer ishlatiladi, qolgan hamma narsa shu yerda.

```
src/Bank.Idm.Domain/          ma'lumot modeli (Identity, Application, Account, Entitlement, Role, AccessRequest, AuditEvent)
src/Bank.Idm.Infrastructure/  EF Core + PostgreSQL (alohida "idm" bazasi), migratsiyalar, AuditLog (xesh zanjiri)
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
cd idm && dotnet build && dotnet test          # testlar uchun Docker kerak
cd idm/portal && npm ci && npm run dev
```

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
| `GET /api/dashboard`, `/api/identities`, `/api/identities/{id}`, `/api/applications`, `/api/audit`, `/api/audit/verify` | admin, auditor |
| `POST /api/identities` | admin |

Migratsiya qo'shish: `cd idm && dotnet ef migrations add <Nom> --project src/Bank.Idm.Infrastructure --startup-project src/Bank.Idm.Infrastructure --output-dir Migrations`
