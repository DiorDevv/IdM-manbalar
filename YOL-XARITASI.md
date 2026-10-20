# Bank IdM: reja

Yangilangan: 2026-10-20.

## 1. IdM tizimi nimalar qila olishi kerak

Zamonaviy IdM (IGA) tizimlarida (SailPoint, Saviynt, Omada, ochiq kodli midPoint) asosan bir xil vazifalar bor.
O'ng ustunda bizning holatimiz.

| # | Vazifa | Nima degani | Bizda |
|---|---|---|---|
| 1 | **Haqiqat manbai (HR)** | Xodim ma'lumoti kadrlar tizimidan keladi, IdM o'zi xodim "o'ylab topmaydi" | ❌ |
| 2 | **Hayot sikli (JML)** | Ishga kirdi → akkauntlar ochiladi; lavozim o'zgardi → ruxsatlar almashadi; ketdi → hammasi yopiladi | ⚠️ faqat AD'dan o'qish va bloklash |
| 3 | **Korrelyatsiya** | HR'dagi xodim ↔ AD, ABS va boshqa tizimlardagi akkauntlari. "Egasiz" akkauntlarni topish | ❌ |
| 4 | **Provisioning (yozish)** | Tizimlarda akkaunt yaratish, bloklash, guruh va rol berish (AD, ABS, pochta…) | ❌ faqat o'qish |
| 5 | **Rol modeli (RBAC)** | Lavozim va bo'limga qarab avtomatik ruxsatlar; rol → tizim ruxsatlari | ❌ |
| 6 | **SoD siyosati** | Bir odamda ziddiyatli ruxsatlar bo'lmasligi (kredit beradi va o'zi tasdiqlaydi) | ❌ |
| 7 | **Ariza va tasdiqlash** | Xodim ruxsat so'raydi → rahbar → resurs egasi → avtomatik beriladi | ❌ |
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

### C. AD konnektori va xodimlar ro'yxati (1 hafta)
- [ ] Konnektor interfeysi (har bir tizim alohida modul): `Read`, keyin `Create/Disable/AddToGroup`.
- [ ] AD konnektori (o'qish): bugungi sozlamalar asosida (LDAPS, `objectGUID`, `whenChanged`, `memberOf`, bloklash filtri).
- [ ] Portal: **Bosh sahifa** (xodimlar soni, bloklanganlar, oxirgi sinxronlash, xatolar), **Xodimlar** ro'yxati va kartochkasi
  (ma'lumotlar, guruhlar, tizimlardagi akkauntlar, tarix).
- **Tayyor:** portalda AD xodimlari va guruhlari ko'rinadi, kartochkada barcha ma'lumot bor.

### D. HR manbai va korrelyatsiya (1–2 hafta)
- [ ] HR importi: avval CSV/Excel yoki HR bazasidan ko'rinish (view), tabel raqami bo'yicha.
- [ ] Korrelyatsiya qoidalari: HR xodim ↔ AD akkaunt (`employeeNumber`), mos kelmaganlar ro'yxati (egasiz akkauntlar, akkauntsiz xodimlar).
- [ ] JML hodisalari: ishga kirish, lavozim o'zgarishi, ketish — portalda hodisalar lentasi.
- **Tayyor:** "egasiz akkauntlar" hisoboti, ishdan ketgan xodimning hamma akkauntlari bir joyda ko'rinadi.

### E. Rollar va SoD (1–2 hafta)
- [ ] Rollar katalogi: biznes roli → tizim ruxsatlari (AD guruhlari, keyin ABS rollari).
- [ ] Avtomatik ("tug'ma") rollar: lavozim + bo'lim → rollar.
- [ ] SoD qoidalari va buzilishlar hisoboti.
- **Tayyor:** har bir xodim uchun "bo'lishi kerak" va "haqiqatda bor" ruxsatlar farqi ko'rinadi.

### F. Ariza va tasdiqlash (1–2 hafta)
- [ ] Xodim katalogdan rol so'raydi → rahbar → resurs egasi tasdiqlaydi; muddat va eslatmalar.
- [ ] SoD tekshiruvi ariza paytida.
- **Tayyor:** ariza boshidan oxirigacha portalda o'tadi, har qadam auditda.

### G. AD'ga yozish (1–2 hafta)
- [ ] AD konnektori: akkaunt yaratish, bloklash, guruhga qo'shish va olib tashlash.
- [ ] Avval "quruq ishga tushirish" (dry-run): nima o'zgarishi ko'rsatiladi, admin tasdiqlaydi. Navbat va qayta urinish.
- [ ] Ishdan ketish: HR'da ketdi → AD'da bloklanadi, guruhlardan chiqariladi.
- **Tayyor:** tasdiqlangan ariza AD'da o'zi bajariladi, ishdan ketgan xodim avtomatik bloklanadi.

### H. Attestatsiya va hisobotlar (1–2 hafta)
- [ ] Kampaniyalar: rahbar o'z xodimlarining ruxsatlarini tasdiqlaydi yoki olib tashlaydi.
- [ ] Hisobotlar (PDF/Excel): kim nimaga kirish huquqiga ega, imtiyozli akkauntlar, SoD buzilishlari, egasiz akkauntlar.

### I. Ishlab chiqarishga tayyorlash
- [ ] MFA (adminlar uchun majburiy), sirlar saqlovchisi, haqiqiy TLS sertifikatlari, zaif paketlar.
- [ ] Audit SIEM'ga, zaxira nusxa, monitoring, ikki nusxada ishlash (HA), yuklama testi (10 000+ xodim).
- [ ] Pentest va bankning axborot xavfsizligi bo'limi tekshiruvi.

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
