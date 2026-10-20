# IdM manbalar: ishga tushirish paketi

Bank IdM tizimi uchun ochiq C# loyihalarni bitta joyga yuklab, build qilib, ishga tushiradi.
Sinov LDAP serveri (`dc=bank,dc=local`) bilan SimpleIdServer'ga xodim va guruhlarni import qilishgacha.

Loyihalar ro'yxati, litsenziyalari va har birining holati: [MANBALAR.md](MANBALAR.md).
Bu repoda faqat skriptlar, sinov ma'lumotlari va skrinshotlar bor; loyihalarning o'zi `setup.sh` bilan GitHub'dan yuklanadi.

## Talablar (Ubuntu/Debian)

```bash
sudo apt install -y git openssl dotnet-sdk-10.0 dotnet-sdk-8.0
```

`npm` ixtiyoriy: frontend kutubxonalari cdnjs'dan yuklanmasa, zaxira sifatida ishlatiladi.

## O'rnatish va ishga tushirish

```bash
cd /home/dior/Projects
git clone https://github.com/DiorDevv/IdM-manbalar.git
cd IdM-manbalar

./setup.sh                          # 16 ta loyihani yuklaydi, patches/ ni qo'llaydi, 8 tasini build qiladi (~5-10 daqiqa)
./install-postgres.sh               # PostgreSQL (Docker, bir marta): IdServer ma'lumotlari restartda saqlanadi
./sinov-ad/install-ad.sh            # sinov Active Directory (Samba AD DC, Docker, bir marta), xodimlar manbai
sudo ./sinov-ldap/install-ldap.sh   # ixtiyoriy: sinov OpenLDAP (LDAP_SOURCE=openldap ./start-all.sh)
./start-all.sh                      # hamma serverlar + LDAP'dan import
./stop-all.sh                       # to'xtatish
```

Loglar `.logs/` papkasida.

## Brauzerda

| UI | Manzil | Kirish |
|---|---|---|
| SimpleIdServer admin paneli | https://localhost:5002/master/users | `administrator` / `password` |
| SimpleIdServer login | https://localhost:5001/master | xuddi shu |
| AuthPermissions (rol va ruxsatlar) | http://localhost:6002 | `Super@g1.com` / `Super@g1.com` |
| SelfService (AD parol portali) | https://localhost:6014 | AD kerak |
| CustomApproval (ariza tasdiqlash) | http://localhost:6005 | Azure AD B2C kerak |
| SCIM API | https://localhost:5003/Schemas, http://localhost:6001/scim/token | API |

Sertifikatlar sinov uchun o'z-o'zidan imzolangan: brauzer ogohlantirishini qabul qiling.
Admin panelda: **Identity provisioning → Automatic Identity Provisioning → LDAP** (sozlamalar, mapping, tarix),
**Users** (Source = LDAP bo'lgan 8 xodim), **Groups** (4 guruh).

## Login sahifasi

- Zamonaviy ko'k-oq dizayn (`ui/wwwroot/styles/bank-theme.css`, `Views/Shared/_FormBuilderLayout.cshtml`), mobilga moslashgan.
  Tashqi shrift yoki CDN yo'q: bank ichki tarmog'ida ham bir xil ko'rinadi. Skrinshotlar: `skrinshotlar/login-*.png`.
- **Admin panel ham o'zbekcha**: 704 ta matn tarjima qilingan (`Global.uz.resx`), tepadagi ro'yxatdan English'ga o'tiladi.
- **Admin panel ko'k-oq mavzuda** (`ui/website-wwwroot/css/bank-admin.css`, `setup.sh` nusxalaydi): to'q ko'k shapka, guruhlangan menyu,
  kartochka jadvallar. Skrinshotlar: `skrinshotlar/admin-*.png`.
- **O'zbek tili standart** (`DefaultLanguage=uz`), ingliz tili tanlanadi. Brauzer tili hisobga olinmaydi, tanlov cookie'da saqlanadi.
- `ui/apply-ui.sh` (`start-all.sh` o'zi chaqiradi) bazani sozlaydi: o'zbekcha tarjimalar, formadan "Register",
  "Forgot my password", Facebook/Google olib tashlanadi, **o'zini o'zi ro'yxatdan o'tkazish yopiladi** (avval
  `/master/Registration?workflowName=pwd` orqali istalgan odam akkaunt ochishi mumkin edi), xato xabari login borligini oshkor qilmaydi.

## Sinov Active Directory (standart xodimlar manbai)

- Samba AD DC, domen `BANK.TEST`, faqat **LDAPS**: `ldaps://127.0.0.1:1636` (CA: `.secrets/ad-ca.pem`).
  Shifrlanmagan 1389 portda simple bind rad etiladi, xuddi bankdagi AD kabi. `localhost` nomi bilan libldap
  sertifikat nomini mos ko'rmaydi, `127.0.0.1` ishlating.
- IdM `svc-idm` xizmat akkaunti bilan o'qiydi (oddiy domen foydalanuvchisi, admin emas). Parollar: `.secrets/ad.env`.
- `OU=Xodimlar`: 8 xodim (OpenLDAP'dagi bilan bir xil, `employeeNumber`, `department`, `title`), `OU=Guruhlar`: 4 guruh.
  Xodim paroli `Parol123!`: IdServer'ga kirishda parolni AD tekshiradi (AD'da bloklangan akkaunt darhol rad etiladi).
- Sinash: `docker exec bank-idm-ad samba-tool user disable d.rahimova`, `... user delete ...`, `... group removemembers ...`.
- Manbani almashtirish: `LDAP_SOURCE=openldap ./start-all.sh`. Almashganda eski manbadan kelgan xodim va guruhlar tozalanadi
  (ID'lar boshqacha, loginlar bir xil).

## Sinov LDAP (OpenLDAP, ixtiyoriy)

- `ldap://localhost:389`, admin `cn=admin,dc=bank,dc=local` / `TestAdmin123`
- `sinov-ldap/bank-test-data.ldif`: 8 xodim (tabel raqami `employeeNumber`, bo'lim, lavozim), 4 guruh. Xodim paroli `Parol123!`.
- `sinov-ldap/sync-ldap.sh`: mapping'ni sozlaydi va extract + import qiladi (`start-all.sh` o'zi chaqiradi).
  Sinxronlash delta: LDAP'da `modifyTimestamp` yoki guruh a'zoligi o'zgargan yozuvlargina qayta import qilinadi.
- **Jadval bo'yicha sinxronlash**: har 15 daqiqada avtomatik (`SYNC_CRON='*/5 * * * *' ./start-all.sh` bilan o'zgartiriladi).
  Admin panel: **Recurring jobs → IdentityProvisioningSyncJob** (to'xtatish yoki qo'lda ishga tushirish).
- **Xodimning hayot sikli** (har sinxronlashda):
  - guruhdan chiqarilgan xodim IdServer'da ham shu guruhdan chiqariladi;
  - LDAP'dan o'chirilgan yoki `employeeType=disabled` qilingan xodim **bloklanadi**, ochiq SSO sessiyalari bekor qilinadi;
  - LDAP'da qayta yoqilsa blok olinadi (admin qo'lda qo'ygan blokka tegilmaydi);
  - xavfsizlik chegarasi: bir martada 20% dan ko'p xodim bloklanishi kerak bo'lsa, hech kim bloklanmaydi va xato logga yoziladi.
  Ishdan ketgan xodimni sinash: `ldapmodify` bilan `employeeType: disabled` qo'ying (README'dagi LDAP admin bilan).

## Muhim eslatmalar

- **Telemetriya o'chirilgan.** SimpleIdServer standart sozlamasida trace'larni muallifning Honeycomb hisobiga
  (`api.honeycomb.io`) yuboradi. `start-all.sh` buni `OpenTelemetryOptions__EnableOtpExported=false` bilan o'chiradi.
  Real muhitda `appsettings.json` dagi bu bo'limni albatta o'zgartiring.
- **libldap.** .NET LDAP kutubxonasi Linux'da `libldap-2.5.so.0` ni qidiradi; `install-ldap.sh` kerak bo'lsa symlink yaratadi.
- **Baza.** `.secrets/postgres.env` bo'lsa (`install-postgres.sh` yaratadi) IdServer PostgreSQL'da ishlaydi:
  foydalanuvchilar, sessiyalar, data-protection kalitlari restartda saqlanadi. Aks holda INMEMORY (hammasi restartda yo'qoladi).
- **Admin paneliga faqat adminlar kiradi** (`SIDS-manager/administrator` roli). Avval LDAP'dan kelgan istalgan xodim kira olardi.
  Boshqa rollar: website'ga `AdminRequiredRoles__0=...` muhit o'zgaruvchisi.
- **SimpleIdServer'ga tuzatishlar** (`patches/`, `setup.sh` o'zi qo'llaydi):
  `stale-returnurl` (restartdan keyingi eski login sahifasi 404 bermaydi), `provisioning-lifecycle` (takroriy guruh xatosi,
  delta, `memberOf`, jadval bo'yicha sync, deprovisioning, a'zolikdan chiqarish, LDAPS), `admin-website` (admin roli talabi,
  401 da token yangilash), `efcore9-alignment` (EF Core 9 / linq2db: busiz PostgreSQL'da LDAP import ishlamaydi). Batafsil: MANBALAR.md.
- Faqat sinov ma'lumotlari bilan ishlating. Real bank AD'siga ulamang.
