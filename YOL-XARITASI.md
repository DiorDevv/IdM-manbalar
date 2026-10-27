# Bank IdM: reja

Yangilangan: 2026-10-26.

## 1. IdM tizimi nimalar qila olishi kerak

Zamonaviy IdM (IGA) tizimlarida (SailPoint, Saviynt, Omada, ochiq kodli midPoint) asosan bir xil vazifalar bor.
O'ng ustunda bizning holatimiz.

| # | Vazifa | Nima degani | Bizda |
|---|---|---|---|
| 1 | **Haqiqat manbai (HR)** | Xodim ma'lumoti kadrlar tizimidan keladi, IdM o'zi xodim "o'ylab topmaydi" | ✅ CSV eksport (haqiqiy HR bilan ulash ochiq) |
| 2 | **Hayot sikli (JML)** | Ishga kirdi → akkauntlar ochiladi; lavozim o'zgardi → ruxsatlar almashadi; ketdi → hammasi yopiladi | ⚠️ hodisalar aniqlanadi, tizimlarda bajarish G'da |
| 3 | **Korrelyatsiya** | HR'dagi xodim ↔ AD, ABS va boshqa tizimlardagi akkauntlari. "Egasiz" akkauntlarni topish | ✅ tabel raqami bo'yicha, 4 hisobot |
| 4 | **Provisioning (yozish)** | Tizimlarda akkaunt yaratish, bloklash, guruh va rol berish (AD, ABS, pochta…) | ❌ faqat o'qish |
| 5 | **Rol modeli (RBAC)** | Lavozim va bo'limga qarab avtomatik ruxsatlar; rol → tizim ruxsatlari | ✅ katalog, avtomatik rollar, farq |
| 6 | **SoD siyosati** | Bir odamda ziddiyatli ruxsatlar bo'lmasligi (kredit beradi va o'zi tasdiqlaydi) | ✅ qoidalar va buzilishlar (ariza paytida — F) |
| 7 | **Ariza va tasdiqlash** | Xodim ruxsat so'raydi → rahbar → resurs egasi → avtomatik beriladi | ✅ IdM'da beriladi, tizimda — G |
| 8 | **Attestatsiya** | Har chorakda rahbar xodimlarining ruxsatlarini tasdiqlaydi yoki olib tashlaydi | ❌ |
| 9 | **Audit va hisobotlar** | Kim, nimaga, qachondan, kim bergan; regulyator (Markaziy bank) uchun hisobotlar | ⚠️ faqat SSO jurnali |
| 10 | **SSO va MFA** | Bitta login bilan barcha ilovalar, ikki bosqichli tasdiqlash | ✅ SSO, ❌ MFA |
| 11 | **Parol xizmatlari** | Parolni o'zi tiklash, parol siyosati | ❌ |
| 12 | **Imtiyozli akkauntlar (PAM)** | Adminlar, xizmat akkauntlari nazorati | ❌ (odatda alohida mahsulot) |

**Xulosa:** hozir bizda "AD'ga ulangan SSO va xodimlar ro'yxati" bor. Bu 10-band va 2-bandning bir qismi.
IdM'ning asosiy qiymati (1, 3, 4, 5, 7, 8, 9-bandlar) hali yo'q.

## 2. Asosiy tavsiya: o'z IdM yadrosi va portali

SimpleIdServer admin paneli — **SSO serverining texnik paneli** (ilovalar, tokenlar, autentifikatsiya sozlamalari).
U IdM paneli emas: unda xodimning tizimlardagi akkauntlari, rollar, arizalar, attestatsiya yo'q. Uni bezash bilan IdM bo'lmaydi.

Shuning uchun:

| Qatlam | Nima | Kim uchun |
|---|---|---|
| **SSO** | SimpleIdServer (bor, sozlangan) | Barcha xodimlar uchun login |
| **SSO texnik paneli** | SimpleIdServer admin (tozalanadi, kam ishlatiladi) | Faqat IT administrator |
| **IdM yadrosi** (yangi) | C#, ASP.NET Core, PostgreSQL: xodimlar, akkauntlar, rollar, arizalar, audit, konnektorlar | — |
| **IdM portali** (yangi) | Zamonaviy veb-interfeys, o'zbekcha, SSO orqali kiriladi | Xodim, rahbar, resurs egasi, IdM administratori, auditor |

**Qaror (2026-10-20):** IdM yadrosi — o'zimizning C# (ASP.NET Core + PostgreSQL); portal — React + TypeScript
(backend bilan REST API orqali). midPoint ishlatilmaydi.

Bugungi ish bekor ketmaydi: AD sozlamalari, sinov AD, PostgreSQL, SSO, login sahifasi, dizayn tizimi
(ko'k-oq mavzu) va topilgan xatolar yangi qismlarda ham ishlatiladi.

## 3. Ketma-ket ish rejasi

Har bosqich kichik va tugallangan: oxirida ishlaydigan natija va "tayyor" mezoni bor. Bosqichlar shu tartibda bajariladi.

### A. Tozalash va tartib (1–2 kun)
- [x] Admin menyusi qayta tuzildi: Foydalanuvchilar / Ilovalar va kirish / Manbalar / Monitoring. Noto'g'ri nomlangan
  "Qo'lda provisioning" va takroriy "Autentifikatsiya" tuzatildi; Federatsiya, Migratsiya, Sertifikat markazlari yashirildi.
- [x] Demo ma'lumotlar o'chirildi (7 ta demo ilova, `administrator-ro`, 2 guruh, SCIM provisioning); `administrator` paroli va
  `SIDS-manager` siri tasodifiy qiymatga almashtirildi (`bootstrap/secure-defaults.sh`, `.secrets/idserver.env`).
- [x] Bloklash muddati o'tgan foydalanuvchi endi "Faol" ko'rinadi.
- **Tayyor:** admin panelda faqat bank uchun kerakli bo'limlar, hammasi to'g'ri nomlangan.

### B. IdM yadrosi poydevori (bajarildi, 2026-10-20)
- [x] `idm/`: ASP.NET Core (.NET 10) Web API, EF Core + PostgreSQL (alohida `idm` bazasi), OpenAPI. Batafsil: [idm/README.md](idm/README.md).
- [x] Ma'lumot modeli: xodim, tizim, akkaunt, ruxsat, rol, rol biriktirish, ariza, tasdiq, audit, portal vakolatlari.
- [x] Portal (React + TypeScript): SSO (OIDC + PKCE) bilan kirish, bosh sahifa, xodimlar ro'yxati va kartochkasi, tizimlar, audit jurnali.
  Vakolatlar IdM bazasida (admin, auditor, kadrlar); oddiy xodim "Ruxsat yo'q" ko'radi.
- [x] Audit jurnali: trigger bilan o'zgartirib/o'chirib bo'lmaydi, SHA-256 xesh zanjiri, portalda "Yaxlitlikni tekshirish".
- [x] Testlar: 14 ta backend (haqiqiy PostgreSQL, Testcontainers) + 3 ta portal; CI (GitHub Actions) har push'da.
- [ ] Ochiq: ilova uchun alohida, faqat INSERT/SELECT huquqli baza roli (I bosqichi); portalda til tanlash (hozir faqat o'zbekcha).

### C. AD konnektori va xodimlar ro'yxati (bajarildi, 2026-10-26)
- [x] Konnektor interfeysi (`Bank.Idm.Connectors`, `IConnector.ReadAsync`); yozish G bosqichida qo'shiladi.
- [x] LDAP/AD konnektori (o'qish): LDAPS, sahifalash, `member;range=`, `objectGUID`, bloklash filtri, `manager`; OpenLDAP ham sozlanadi.
- [x] Sinxronlash: akkaunt/guruh/a'zolik solishtiriladi, har bir o'zgarish auditda, tabel raqami bo'yicha korrelyatsiya,
  20% o'chirish chegarasi (administrator tasdig'i bilan), ishga tushishlar tarixi, jadval (15 daqiqa) va qo'lda ishga tushirish.
- [x] Portal: bosh sahifada sinxronlash holati va bloklangan akkauntlar; **tizim kartochkasi** (akkauntlar, egasizlar filtri,
  guruhlar, sinxronlash tarixi, "Sinxronlash" tugmasi); xodim kartochkasida guruhlar va **tarix**.
- [x] Testlar: 29 ta backend (sinxronlash, API, LDAP xaritalash, haqiqiy OpenLDAP'ga qarshi) + 6 ta portal.
- [ ] Ochiq: sinov AD (Samba) bilan jonli tekshiruv (bu mashinada Docker yo'q, OpenLDAP bilan tekshirildi); ichma-ich guruhlar;
  delta sinxronlash (`whenChanged`, 10 000+ xodimda kerak bo'lsa); bir nechta DC.
- **Tayyor:** portalda AD xodimlari va guruhlari ko'rinadi, kartochkada barcha ma'lumot bor.

### D. HR manbai va korrelyatsiya (bajarildi, 2026-10-26)
- [x] HR importi: CSV eksport (ustun nomlari sozlanadi), qat'iy tekshiruv (takror tabel, majburiy ustun, UTF-8), jadval va qo'lda.
- [x] HR haqiqat manbai: HR'dagi xodim AD manbalidan HR manbaliga o'tadi; HR ulanganda AD xodim yaratmaydi.
- [x] Korrelyatsiya hisobotlari: ishdan ketgan, lekin akkaunti faol; egasiz akkauntlar; akkauntsiz xodimlar; HR'da yo'q xodimlar.
- [x] JML hodisalari lentasi (portal "Hodisalar"), Toshkent vaqti bo'yicha sanalar, ommaviy ishdan ketishda to'xtash.
- [ ] Ochiq: bankning haqiqiy HR tizimi bilan ulash (fayl joyi va formati yoki bazadagi view) — HR bo'limi bilan kelishiladi.
- **Tayyor:** "egasiz akkauntlar" hisoboti, ishdan ketgan xodimning hamma akkauntlari bir joyda ko'rinadi.

### E. Rollar va SoD (bajarildi, 2026-10-26)
- [x] Rollar katalogi: rol → tizim ruxsatlari (AD guruhlari), xavf darajasi, ariza orqali so'raladimi.
- [x] Avtomatik ("tug'ma") rollar: bo'lim + lavozim qoidalari, sinxronlashdan keyin va qo'lda qayta hisoblash, tarix saqlanadi.
- [x] "Bo'lishi kerak" va "haqiqatda bor" farqi: xodim kartochkasida va "Ruxsatlar farqi" hisobotida.
- [x] SoD qoidalari (ruxsat darajasida, ikki tomon) va buzilishlar hisoboti; rolning o'zi qoidani buzsa ogohlantirish.
- [x] Portal: Rollar, rol kartochkasi (ruxsatlar, qoidalar, xodimlar), SoD sahifasi; dizayn tizimi (`.claude/skills/idm-portal-ui`).
- [ ] Ochiq: SoD istisnolari (vaqtinchalik ruxsat, kompensatsion nazorat), rol egasi (F bosqichida tasdiqlovchi sifatida).
- **Tayyor:** har bir xodim uchun "bo'lishi kerak" va "haqiqatda bor" ruxsatlar farqi ko'rinadi.

### F. Ariza va tasdiqlash (bajarildi, 2026-10-26)
- [x] Xodim katalogdan rol so'raydi (o'zi yoki xodimlari uchun) → rahbar → rol egasi → SoD bo'lsa axborot xavfsizligi; muddatli ruxsat.
- [x] SoD tekshiruvi ariza paytida (oldindan ko'rish va qo'shimcha bosqich), o'zini tasdiqlash taqiqi, parallel qarorlardan himoya.
- [x] Portal: arizalar (tasdiqlash navbati, mening arizalarim, hammasi), yangi ariza (katalog, jonli zanjir), ariza kartochkasi.
  Oddiy xodim portalda faqat arizalar bo'limini ko'radi.
- [x] Ishdan ketganda barcha rollar tugaydi va kutilayotgan arizalar bekor qilinadi.
- [ ] Ochiq: eslatmalar va muddati o'tgan arizalar (pochta/xabarnoma kanali kerak), tasdiqlovchini almashtirish (ta'til), rol egasini portalda belgilash.
- **Tayyor:** ariza boshidan oxirigacha portalda o'tadi, har qadam auditda.

### G. AD'ga yozish (bajarildi, 2026-10-26)
- [x] AD/LDAP konnektori: akkaunt yaratish (bloklangan, parolsiz), bloklash, guruhga qo'shish va olib tashlash — idempotent,
  o'zgarmas identifikator bo'yicha, faqat ruxsat etilgan OU ichida, alohida yozish akkaunti bilan.
- [x] Dry-run (`Approval`): reja portalda, administrator tanlab tasdiqlaydi; `Automatic` rejimda ham ko'p amal chiqsa — tasdiqqa.
  Navbat, kechikishli qayta urinish, doimiy xatoda to'xtash, keraksiz bo'lib qolgan amallarni bekor qilish.
- [x] Ishdan ketish: HR'da ketdi → rollar tugaydi → AD'da bloklanadi va barcha guruhlardan chiqariladi.
- [x] Rol olib tashlansa faqat IdM qo'shgan a'zolik olinadi; tasdiqlangan ariza guruh haqiqatda paydo bo'lgach "Bajarildi".
- [x] Portal: "Tizimga yozish" sahifasi (tizimlar rejimi, reja, tasdiq / qayta urinish / yopish, holatlar).
- [x] Jonli sinov (OpenLDAP + HR fayli): ishdan ketgan xodim bloklandi, yangi xodimga akkaunt yaratilib rol guruhiga qo'shildi.
- [ ] Ochiq: bankning haqiqiy AD'sida sinov (LDAPS, alohida yozish akkaunti va huquqlari), yangi akkauntni ishga kirish kunida
  yoqish va parolni xavfsiz yetkazish (helpdesk jarayoni), akkauntni qayta yoqish (qayta ishga olish), ABS va boshqa tizimlar konnektorlari.
- **Tayyor:** tasdiqlangan ariza AD'da o'zi bajariladi, ishdan ketgan xodim avtomatik bloklanadi.

### H. Attestatsiya va hisobotlar (bajarildi, 2026-10-26)
- [x] Kampaniyalar: rahbar o'z xodimlarining ariza/qo'lda berilgan rollari va asossiz guruhlarini qoldiradi yoki olib tashlaydi;
  rahbarsiz xodimlar — administratorlar; o'zini tasdiqlash taqiqi; oldindan ko'rish; yakunlash va bekor qilish.
- [x] Olib tashlash bajariladi: rol darhol tugaydi, guruh tizimga yozish orqali olinadi va "tizimdan olindi" deb kuzatiladi.
- [x] Hisobotlar (Excel va PDF/chop etish): kim nimaga kirish huquqiga ega, imtiyozli akkauntlar, SoD buzilishlari, egasiz akkauntlar,
  ishdan ketganlar, kampaniya natijasi. Har bir eksport auditda.
- [ ] Ochiq: rahbarlarga eslatma (pochta kanali), tekshiruvchini almashtirish, muddati o'tganda ko'rib chiqilmaganini avtomatik olib
  tashlash siyosati, rol egalari tomonidan rol tarkibini attestatsiya qilish, server tomonida PDF (imzo bilan).
- **Tayyor:** rahbar portalda xodimlarining ruxsatlarini ko'rib chiqadi, olib tashlangan ruxsat tizimdan o'zi olinadi, auditor
  hisobotlarni Excel yoki PDF ko'rinishida oladi.

### I. Ishlab chiqarishga tayyorlash (kod qismi bajarildi, 2026-10-27)
- [x] MFA: admin/auditor vakolati faqat ikkinchi omil bilan (`amr`/`acr`), portal step-up; SimpleIdServer access token'ga acr/amr (patch).
- [x] Sirlar katalogi (Vault Agent / Kubernetes secrets); production sozlama nazorati — TLS/LDAPS/VerifyFull'siz, fayldagi sir bilan
  yoki proksisiz ochiq HTTP bilan server ishga tushmaydi; HSTS, ishonchli proksi.
- [x] Zaif paketlar: vite yangilandi, CI'da NuGet va npm tekshiruvi.
- [x] Audit SIEM'ga (syslog TLS, xesh bilan, yo'qotishsiz), Prometheus metrikalari, `/health`.
- [x] HA: yetakchi tanlash, bir tizimda bitta sinxronlash, to'xtagan nusxa ishlarini yopish (jonli sinov: 2 s'da yetakchi almashdi).
- [x] Yuklama testi: 10 000 va 50 000 xodim; topilgan sekinlik (attestatsiya 60 s) va xotira (Excel 1.7 GB) tuzatildi.
- [x] Zaxira nusxa va tiklashni tekshirish skriptlari, `verify-audit`, PostgreSQL eng kam huquqli rollar (sinovdan o'tgan).
- [x] Axborot xavfsizligi bo'limi uchun hujjat: tahdidlar va nazoratlar, pentest va chiqarish ro'yxatlari (`idm/XAVFSIZLIK.md`).
- [ ] Bank tomonida: haqiqiy TLS sertifikatlari va proksi, Vault, SSO'da MFA (ACR) sozlash, bank AD'sida sinov, pentest,
  axborot xavfsizligi bo'limi tekshiruvi va tasdig'i, RTO/RPO kelishuvi.

## 4. Bajarilgan ishlar

**2026-10-19 — 2026-10-20: SSO va AD poydevori**
- Barqarorlashtirish: restartdan keyin login, PostgreSQL, data-protection kalitlari, EF Core 9 / linq2db mosligi.
- AD'dan o'qish: jadval bo'yicha sinxronlash, delta, deprovisioning (20% chegara, sessiyalarni yopish),
  a'zolik o'zgarishi, qayta ishga olish, `objectGUID`/`objectSid`, LDAPS bilan login.
- Sinov AD (Samba AD DC, Docker), xizmat akkaunti, 8 xodim va 4 guruh.
- Xavfsizlik: admin paneliga faqat admin roli, o'zini ro'yxatdan o'tkazish yopildi, bo'sh parol rad etiladi,
  login mavjudligi oshkor qilinmaydi.
- Login sahifasi va admin panel: ko'k-oq dizayn, o'zbek tili (standart), mobil ko'rinish.
- SimpleIdServer'da 20 dan ortiq xato topildi va tuzatildi (`patches/`, batafsil: MANBALAR.md).

Ochiq qolganlar: bankning haqiqiy AD'si, katta guruhlar (`member;range=`), bir nechta DC, rus tili,
deprovisioning hodisalari auditda ko'rinmaydi, provisioning tarixini tozalash siyosati.
