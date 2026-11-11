# Bank IdM: reja

Yangilangan: 2026-11-03.

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
- [x] Ilova uchun alohida, faqat INSERT/SELECT huquqli baza roli — I bosqichida (`deploy/db-roles.sql`).
- [x] Portalda til tanlash (2026-11-03): o'zbekcha (standart) va ruscha, shapkadagi tugma, tanlov brauzerda saqlanadi.
  Server xato xabarlari hozircha o'zbekcha.

### C. AD konnektori va xodimlar ro'yxati (bajarildi, 2026-10-26)
- [x] Konnektor interfeysi (`Bank.Idm.Connectors`, `IConnector.ReadAsync`); yozish G bosqichida qo'shiladi.
- [x] LDAP/AD konnektori (o'qish): LDAPS, sahifalash, `member;range=`, `objectGUID`, bloklash filtri, `manager`; OpenLDAP ham sozlanadi.
- [x] Sinxronlash: akkaunt/guruh/a'zolik solishtiriladi, har bir o'zgarish auditda, tabel raqami bo'yicha korrelyatsiya,
  20% o'chirish chegarasi (administrator tasdig'i bilan), ishga tushishlar tarixi, jadval (15 daqiqa) va qo'lda ishga tushirish.
- [x] Portal: bosh sahifada sinxronlash holati va bloklangan akkauntlar; **tizim kartochkasi** (akkauntlar, egasizlar filtri,
  guruhlar, sinxronlash tarixi, "Sinxronlash" tugmasi); xodim kartochkasida guruhlar va **tarix**.
- [x] Testlar: 29 ta backend (sinxronlash, API, LDAP xaritalash, haqiqiy OpenLDAP'ga qarshi) + 6 ta portal.
- [x] Ichma-ich guruhlar (2026-11-03): meros a'zolik qaysi guruh orqali ekani bilan saqlanadi; SoD va hisobotlarda hisobga olinadi,
  tizimga yozish va attestatsiya faqat bevosita a'zolikni o'zgartiradi.
- [x] Bir nechta DC (2026-11-03): tartibli ro'yxat, ishlamayotganidan keyingisiga o'tish, noto'g'ri parolda to'xtash.
- [x] Sinov AD (Samba AD DC 4.25) bilan jonli tekshiruv (2026-11-03, `AdLiveTests`, 5 ta): LDAPS, objectGUID, userAccountControl,
  ichma-ich guruh, 1600 akkaunt va 1600 a'zoli guruh, noto'g'ri parol, 389'da simple bind rad etilishi; eng kam huquqli
  `svc-idm-write` bilan yaratish/bloklash/yoqish/a'zolik va to'liq zanjir (sinxronlash → ishdan ketish → AD'da bloklash → tasdiq).
  Topilma va tuzatish: Samba parolsiz akkauntni yoqishga ruxsat beradi — endi yaratishda tasodifiy noma'lum parol qo'yiladi.
- [x] Delta sinxronlash (`whenChanged`) — kerak emas deb qaror qilindi: yuklama testiga ko'ra 50 000 xodimda to'liq qayta sinxronlash 5 s.
- **Tayyor:** portalda AD xodimlari va guruhlari ko'rinadi, kartochkada barcha ma'lumot bor.

### D. HR manbai va korrelyatsiya (bajarildi, 2026-10-26)
- [x] HR importi: CSV eksport (ustun nomlari sozlanadi), qat'iy tekshiruv (takror tabel, majburiy ustun, UTF-8), jadval va qo'lda.
- [x] HR haqiqat manbai: HR'dagi xodim AD manbalidan HR manbaliga o'tadi; HR ulanganda AD xodim yaratmaydi.
- [x] Korrelyatsiya hisobotlari: ishdan ketgan, lekin akkaunti faol; egasiz akkauntlar; akkauntsiz xodimlar; HR'da yo'q xodimlar.
- [x] JML hodisalari lentasi (portal "Hodisalar"), Toshkent vaqti bo'yicha sanalar, ommaviy ishdan ketishda to'xtash.
- [x] Filial va o'tish (mover) siyosati (2026-11-11): filial HR'dan, avtomatik rol qoidasida filial; filial yoki bo'lim o'zgarsa ariza/qo'lda
  berilgan rollar yangi rahbar qaroriga (7 kun), qoldirilmasa avtomatik olinadi; eski arizalar bekor; ommaviy o'tishda import to'xtaydi.
  Topilgan va tuzatilgan xato: ishdan ketgan xodimning muddatli roli muddati tugaguncha qolardi.
- [ ] Ochiq: bankda HR manbai — iABS (Fido-Biznes): kadrlar eksporti yoki Oracle view / iABS 7 servis shinasi orqali konnektor.
- [ ] Ochiq: bankning haqiqiy HR tizimi bilan ulash (fayl joyi va formati yoki bazadagi view) — HR bo'limi bilan kelishiladi.
- **Tayyor:** "egasiz akkauntlar" hisoboti, ishdan ketgan xodimning hamma akkauntlari bir joyda ko'rinadi.

### E. Rollar va SoD (bajarildi, 2026-10-26)
- [x] Rollar katalogi: rol → tizim ruxsatlari (AD guruhlari), xavf darajasi, ariza orqali so'raladimi.
- [x] Avtomatik ("tug'ma") rollar: bo'lim + lavozim qoidalari, sinxronlashdan keyin va qo'lda qayta hisoblash, tarix saqlanadi.
- [x] "Bo'lishi kerak" va "haqiqatda bor" farqi: xodim kartochkasida va "Ruxsatlar farqi" hisobotida.
- [x] SoD qoidalari (ruxsat darajasida, ikki tomon) va buzilishlar hisoboti; rolning o'zi qoidani buzsa ogohlantirish.
- [x] Portal: Rollar, rol kartochkasi (ruxsatlar, qoidalar, xodimlar), SoD sahifasi; dizayn tizimi (`.claude/skills/idm-portal-ui`).
- [x] SoD istisnolari (2026-11-03): muddatli (≤ 1 yil), asos va kompensatsion nazorat bilan, o'ziga berish taqiqlangan; muddat
  tugasa buzilish yana ochiq; portal, hisobot va auditda.
- [x] Rol egasi portalda belgilanadi (2026-11-03) — F bosqichida tasdiqlovchi.
- **Tayyor:** har bir xodim uchun "bo'lishi kerak" va "haqiqatda bor" ruxsatlar farqi ko'rinadi.

### F. Ariza va tasdiqlash (bajarildi, 2026-10-26)
- [x] Xodim katalogdan rol so'raydi (o'zi yoki xodimlari uchun) → rahbar → rol egasi → SoD bo'lsa axborot xavfsizligi; muddatli ruxsat.
- [x] SoD tekshiruvi ariza paytida (oldindan ko'rish va qo'shimcha bosqich), o'zini tasdiqlash taqiqi, parallel qarorlardan himoya.
- [x] Portal: arizalar (tasdiqlash navbati, mening arizalarim, hammasi), yangi ariza (katalog, jonli zanjir), ariza kartochkasi.
  Oddiy xodim portalda faqat arizalar bo'limini ko'radi.
- [x] Ishdan ketganda barcha rollar tugaydi va kutilayotgan arizalar bekor qilinadi.
- [x] Tasdiqlovchini almashtirish (2026-11-03): muddatli o'rinbosar (ta'til), arizalar va attestatsiya uchun, auditda "kimning o'rniga".
- [x] Rol egasini portalda belgilash (2026-11-03).
- [x] Eslatmalar (2026-11-03): pochta orqali yangi bosqich, kechikkan bosqich eslatmasi, ariza natijasi (o'rinbosarga ham).
- [ ] Bank tomonida: SMTP server va xizmat akkaunti, xodimlar emaili HR'da to'liq bo'lishi.
- **Tayyor:** ariza boshidan oxirigacha portalda o'tadi, har qadam auditda.

### G. AD'ga yozish (bajarildi, 2026-10-26)
- [x] AD/LDAP konnektori: akkaunt yaratish (bloklangan, AD'da tasodifiy noma'lum parol bilan), bloklash, guruhga qo'shish va olib tashlash — idempotent,
  o'zgarmas identifikator bo'yicha, faqat ruxsat etilgan OU ichida, alohida yozish akkaunti bilan.
- [x] Dry-run (`Approval`): reja portalda, administrator tanlab tasdiqlaydi; `Automatic` rejimda ham ko'p amal chiqsa — tasdiqqa.
  Navbat, kechikishli qayta urinish, doimiy xatoda to'xtash, keraksiz bo'lib qolgan amallarni bekor qilish.
- [x] Ishdan ketish: HR'da ketdi → rollar tugaydi → AD'da bloklanadi va barcha guruhlardan chiqariladi.
- [x] Rol olib tashlansa faqat IdM qo'shgan a'zolik olinadi; tasdiqlangan ariza guruh haqiqatda paydo bo'lgach "Bajarildi".
- [x] Portal: "Tizimga yozish" sahifasi (tizimlar rejimi, reja, tasdiq / qayta urinish / yopish, holatlar).
- [x] Jonli sinov (OpenLDAP + HR fayli): ishdan ketgan xodim bloklandi, yangi xodimga akkaunt yaratilib rol guruhiga qo'shildi.
- [x] Qayta yoqish (2026-11-03): qayta ishga olingan yoki to'xtatilishi tugagan xodim akkaunti — faqat IdM o'zi bloklagan bo'lsa;
  IdM yaratgan akkaunt — ishga kirish kuni (`EnableNewAccounts`, AD'da avval parol kerak). Portal va auditda "Akkauntni yoqish".
- [ ] Ochiq: bankning haqiqiy AD'sida sinov (LDAPS, alohida yozish akkaunti va huquqlari), parolni xavfsiz yetkazish
  (helpdesk jarayoni), ABS va boshqa tizimlar konnektorlari.
- **Tayyor:** tasdiqlangan ariza AD'da o'zi bajariladi, ishdan ketgan xodim avtomatik bloklanadi.

### H. Attestatsiya va hisobotlar (bajarildi, 2026-10-26)
- [x] Kampaniyalar: rahbar o'z xodimlarining ariza/qo'lda berilgan rollari va asossiz guruhlarini qoldiradi yoki olib tashlaydi;
  rahbarsiz xodimlar — administratorlar; o'zini tasdiqlash taqiqi; oldindan ko'rish; yakunlash va bekor qilish.
- [x] Olib tashlash bajariladi: rol darhol tugaydi, guruh tizimga yozish orqali olinadi va "tizimdan olindi" deb kuzatiladi.
- [x] Hisobotlar (Excel va PDF/chop etish): kim nimaga kirish huquqiga ega, imtiyozli akkauntlar, SoD buzilishlari, egasiz akkauntlar,
  ishdan ketganlar, kampaniya natijasi. Har bir eksport auditda.
- [x] Muddati o'tganda ko'rib chiqilmaganini avtomatik olib tashlash siyosati (2026-11-03, kampaniya bo'yicha tanlanadi).
- [x] Tekshiruvchini almashtirish — o'rinbosarlik orqali (F bosqichi, 2026-11-03).
- [x] Rahbarlarga eslatma (2026-11-03): kampaniya boshlanganda va muddat yaqinlashganda, pochta orqali.
- [x] Rol tarkibi attestatsiyasi (2026-11-03): rol egasi ruxsatlarni tasdiqlaydi yoki olib tashlaydi, muddat — 365 kun.
- [ ] Bank tomonida qaror: server tomonida imzolangan hisobot — imzo turi (E-IMZO yoki bank PKI), format (PDF/PAdES yoki alohida CMS) va sertifikat.
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
- [x] So'rovlar chegarasi (2026-11-06): login/IP bo'yicha, og'ir amallar navbati, so'rov vaqt chegarasi (`Idm:Limits`), 429/504 portalda.
- [x] Bir vaqtdagi yuklama testi (2026-11-06, k6, `idm/tests/load`, 10 000 xodim): 300 foydalanuvchi — sahifa p95 13 ms, 1000 foydalanuvchi
  30 s da kirganda — p95 28 ms, 5xx yo'q, bazaga ≤ 16 ulanish. Topilma va tuzatish: attestatsiya navbati 500 yozuv (320 KB) → xodimlar bo'yicha
  sahifalash, JSON siqish (trafik 1.1 GB → 186 MB), bosh sahifa hisoblari 30 s keshda.
- [ ] Bank tomonida: haqiqiy TLS sertifikatlari va proksi, Vault, SSO'da MFA (ACR) sozlash, bank AD'sida sinov, pentest,
  axborot xavfsizligi bo'limi tekshiruvi va tasdig'i, RTO/RPO kelishuvi.

### J. One Identity Manager darajasiga chiqish (2026-11-11 dan)
Bank uchun kerakli OIM funksiyalari (SAP, PAM, bulut konnektorlari — kerak emas). Taxminan 3–4 oy.
- [x] 1. Ikki kishilik tasdiq va portal vakolatlarini boshqarish (2026-11-11): xavfli administrator amallari ikkinchi administrator tasdig'i bilan.
- [ ] 2. Universal SQL/REST konnektor, ustida iABS va 1C (bank ma'lumoti kerak: iABS 7 servis shinasi yoki Oracle view).
- [ ] 3. Tashkilot daraxti: filial → bo'lim → sho''ba, rollar daraxt bo'ylab meros.
- [ ] 4. Dinamik rollar: shartli qoidalar va istisnolar.
- [ ] 5. Sozlanadigan tasdiqlash siyosatlari (xavf, tizim, rolga qarab; parallel va ketma-ket).
- [ ] 6. Risk indeksi: rol, ruxsat, xodim.
- [ ] 7. Tarixiy holat: "sanada kimda nima bor edi" hisoboti.
- [ ] 8. Peer-group tahlili: bo'limdoshlarida yo'q huquq — attestatsiyada belgi.
- [ ] 9. Account definitions: tizim bo'yicha akkaunt shabloni va boshqaruv darajasi.
- [ ] Keyinroq: role mining, konnektorlarni UI'dan sozlash, parolni o'zi tiklash (bankda mavjudligi aniqlanadi).

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

## 5. Real bank IdM darajasiga chiqish (2026-11-03 holati)

Kod tomoni pilotga tayyor; asosiy farq — boshqariladigan tizimlar kam (faqat AD/LDAP va HR CSV) va bank muhitida sinalmagan.

### Ma'lum, hali sinalmagan yoki zaif joylar
- Portal brauzerda ko'zdan kechirilmagan (faqat testlar, tip tekshiruvi, build).
- `member;range` (1500+ a'zoli guruh) — faqat haqiqiy Windows AD'da tekshiriladi (Samba bitta javobda beradi).
- SMTP jonli serverda sinalmagan; 465 (implicit TLS) qo'llanmaydi, faqat 587 STARTTLS.
- Katta kampaniyada "ko'rib chiqilmaganlarni olib tashlash" har yozuvga alohida audit — yuklama testida yo'q.
- API xato xabarlari faqat o'zbekcha; rol tarkibi muddati kelganda egasiga pochta xabari yo'q.
- SimpleIdServer'ga 20+ o'z patchimiz — yangilanishlarda qo'llab-quvvatlash xavfi (Keycloak kabi muqobilni AX bilan ko'rib chiqish).

### Real IGA'dan funksional farqlar (ustuvorlik bo'yicha)
1. **Konnektorlar:** ABS (core banking), pochta (Exchange), ma'lumotlar bazalari, ichki ilovalar; umumiy SCIM/REST konnektor;
   API'si yo'q tizimlar uchun **"qo'lda bajarish"** (tizim administratoriga topshiriq → bajarib portalda tasdiqlaydi).
2. **Mover attestatsiyasi:** bo'lim/lavozim o'zgarganda yangi rahbarga eski (ariza bilan olingan) ruxsatlarni ko'rib chiqish.
3. Parolni o'zi tiklash xizmati; PAM bilan integratsiya (odatda alohida mahsulot).
4. Yangi xodimga parolni yetkazish (helpdesk) jarayoni — bank bilan kelishish.
5. Rol "qazib olish" (mavjud ruxsatlardan rollarni taklif qilish).
6. Xodimning o'z kabineti: "mening ruxsatlarim", ruxsatdan o'zi voz kechish.

### Ishlab chiqarish uchun (texnik bo'lmagan qism ham)
- Me'yoriy: Markaziy bank talablari, shaxsga doir ma'lumotlar qonuni (jumladan O'zbekistonda saqlash), saqlash va o'chirish muddatlari — yuristlar va AX bilan.
- Ekspluatatsiya: o'rnatish paketi (konteyner yoki systemd), Prometheus ogohlantirishlari, tiklash mashqlari, runbook'lar, navbatchi jamoa.
- Mustaqil pentest va tashqi kod tekshiruvi; rahbarlar va administratorlar uchun qisqa qo'llanmalar.

### Tavsiya etilgan tartib
1. Pilotga tayyorlash (2–4 hafta): bank AD'sida sinov OU va akkauntlar, HR eksporti, `AdLiveTests` bank AD'siga qarshi, portalni ko'rib chiqish.
2. Pilot — faqat o'qish (1 oy, bitta bo'lim yoki filial): korrelyatsiya hisobotlari va SoD, yozish o'chiq.
3. Yozish `Approval` rejimida: avval ishdan ketganlarni bloklash, keyin rol guruhlari; ishonch hosil bo'lgach `Automatic` (chegara bilan).
4. Kengaytirish: ABS va "qo'lda bajarish" konnektorlari, mover attestatsiyasi, birinchi chorak attestatsiyasi.
5. I bosqichining bank qismini yopish va to'liq ishga tushirish.

## 6. Zamonaviy IGA va Markaziy bank talablari bo'yicha reja (2026-11-06)

Manba: Gartner 2026 IGA Market Guide (vendor xulosalari), SailPoint, Microsoft Entra ID Governance, midPoint; Markaziy bankning
tijorat banklari uchun axborot va kiberxavfsizlik minimal talablari nizomi (AV 18.08.2025, № 3669, 7-bob, 37–49-bandlar).
Bozor yo'nalishi: chorakda bir marta qo'lda attestatsiyadan uzluksiz, hodisaga asoslangan boshqaruvga; inson bo'lmagan
(xizmat) akkauntlar; imtiyozli huquqni vaqtincha berish (JIT); tavsiyalar va g'ayrioddiy huquqlarni aniqlash.

| Bosqich | Nima | Asos |
|---|---|---|
| **J. Uzluksiz nazorat** | Oylik avtomatik attestatsiya (jadval) va natija hujjati; mover: lavozim/bo'lim o'zgarganda eski ariza rollari yangi rahbarga ko'rib chiqishga, avtomatik rollar almashadi; IdM'dan tashqari qilingan o'zgarish (AD'da qo'lda qo'shilgan guruh, ayniqsa imtiyozli) — ogohlantirish va mikro-attestatsiya yoki qaytarish | MB 40, 41, 44-bandlar; Gartner "continuous" |
| **K. Konnektorlar** | "Qo'lda bajarish" (tizim administratoriga topshiriq → portalda tasdiq), umumiy SQL/REST/SCIM konnektor, ABS | MB 37, 40 (barcha tizimlar) |
| **L. Imtiyozli va xizmat akkauntlari** | Xizmat akkauntining egasi va maqsadi, egasiz xizmat akkauntlari hisoboti, davriy ko'rib chiqish; imtiyozli rolni muddatli (soatlab) berish — JIT; PAM bilan integratsiya (ro'yxat, sessiya auditiga havola) | MB 42, 48, 49; NHI trendi |
| **M. Tahlil va tavsiyalar** | Hamkasblar guruhi bo'yicha g'ayrioddiy huquqlar (outlier), ortiqcha huquqlar; ariza va attestatsiyada "hamkasblarning 90% da bor/yo'q" tavsiyasi; rollarni qazib olish | MB 42; SailPoint/Entra tavsiyalari |
| **N. O'z-o'ziga xizmat va hayot sikli** | "Mening ruxsatlarim", ruxsatdan voz kechish; ishga kirishdan oldingi va ishdan ketgandagi vazifalar (egalikni topshirish, rahbarga xabar) | Entra lifecycle workflows |
| **O. Kirish konteksti** | Ish vaqtidan tashqari kirish taqiqi, to'lov huquqi borlarga qurilma (token) talabi — SSO siyosati bilan | MB 45–47 |

Bank tomonida: MB nizomi 7-bobini AX bilan band-band solishtirish (IP/MAC audit — 43-band, PAM — 48-band).
