# Bank IdM: hozirgi daraja va yo'l xaritasi

Holat sanasi: 2026-10-19.

## Hozirgi daraja: sinov stendi (PoC)

Hozir bor narsa:

| Qism | Holat |
|---|---|
| SSO (OpenID Connect) va admin panel | SimpleIdServer ishlaydi, PostgreSQL'da saqlanadi |
| LDAP'dan o'qish (sinov `dc=bank,dc=local`) | 8 xodim, 4 guruh. Delta sinxronlash ishlaydi |
| SCIM server | Alohida ishlaydi, IdM oqimiga ulanmagan |
| Rol/ruxsat, ariza portali, AD parol portali | Namuna sifatida alohida ishlaydi (AuthPermissions, CustomApproval, SelfService), bir-biriga ulanmagan |

IdM'ning asosiy vazifalari hali yo'q:

- Xodimning hayot sikli: ishga kirish, lavozim o'zgarishi, ishdan ketish (Joiner/Mover/Leaver).
- HR tizimining "haqiqat manbai" (authoritative source) sifatida ulanishi.
- Maqsadli tizimlarga yozish: AD'da akkaunt yaratish yoki bloklash, ABS va boshqa tizimlar.
- Rol modeli (lavozim + bo'lim → rollar) va SoD (vazifalar ziddiyati) qoidalari.
- Ruxsat so'rash va tasdiqlash jarayoni.
- Davriy qayta tasdiqlash (attestatsiya) va hisobotlar.
- Bank darajasidagi xavfsizlik va ishonchlilik.

## Yo'l xaritasi

Har bir bosqich oldingisiga tayanadi. Bajarilganda `[x]` qo'yiladi.

### 0. Barqarorlashtirish (bajarildi, 2026-10-19)
- [x] Restartdan keyin login 404 bermaydi.
- [x] PostgreSQL: ma'lumotlar va kalitlar restartda saqlanadi.
- [x] LDAP: takroriy guruh xatosi tuzatildi, delta sinxronlash ishlaydi, `memberOf` bir nechta guruh bilan ishlaydi.
- [x] Admin panel token muddati o'tganda o'zi yangilaydi. EF Core/linq2db versiya ziddiyati tuzatildi.

### 1. AD'dan o'qishni to'liq qilish (faqat o'qish), 2026-10-19
- [x] Sinxronlash jadval bo'yicha (standart har 15 daqiqa), import va deprovisioning avtomatik.
- [x] Deprovisioning: o'chirilgan yoki bloklangan xodim IdM'da bloklanadi, SSO sessiyalari bekor qilinadi, 20% xavfsizlik chegarasi.
- [x] Guruh a'zoligi o'zgarishi: qo'shilgan va olib tashlangan a'zoliklar sinxronlanadi.
- [x] Admin paneliga faqat admin roli bilan kirish (sinov paytida topilgan xavfsizlik teshigi).
- [x] Sinov AD (Samba AD DC, Docker): LDAPS, xizmat akkaunti, `objectGUID`, `whenChanged`, `memberOf`, `userAccountControl`.
  Barcha ssenariylar AD'da qayta sinaldi, AD paroli bilan login ishlaydi, qayta ishga olish hal qilindi. 6 ta yangi xato tuzatildi.
- [ ] Bankning haqiqiy sinov AD'si: CA sertifikati, xizmat akkaunti, OU tuzilmasi bankdan olinadi (kodda o'zgarish kutilmaydi).
  Katta guruhlar uchun `member;range=` va bir nechta DC (failover) hali sinalmagan.
- [ ] Ochiq savollar: deprovisioning jurnali admin paneldagi Auditing'da ko'rinmaydi (hozircha faqat log);
  provisioning tarixi (`IdentityProvisioningHistory`) har sync'da o'sadi, tozalash siyosati kerak.

### Login sahifasi (2026-10-20)
- [x] Zamonaviy ko'k-oq dizayn, mobil ko'rinish, o'zbek tili (standart) va ingliz tili; consent va xato sahifalari ham.
- [x] Xavfsizlik: o'zini o'zi ro'yxatdan o'tkazish yopildi, demo Facebook/Google olib tashlandi, login mavjudligi oshkor qilinmaydi.
- [x] Admin panel ko'k-oq professional mavzuda (shapka, menyu, jadvallar, formalar, belgilar).
- [x] Admin panelning o'zbekcha tarjimasi (704 ta matn + koddagi qattiq yozilgan sarlavhalar), standart til o'zbekcha.
- [ ] Rus tili (bank xodimlari uchun kerak bo'lsa). Audit yozuvlari va vazifa holatlari serverdan inglizcha keladi.

### 2. HR'ni haqiqat manbai qilish
- [ ] HR tizimidan (kadrlar bazasi) xodimlarni tabel raqami bo'yicha import qilish.
- [ ] HR ↔ AD moslashtirish (korrelyatsiya), mos kelmaganlar hisoboti (masalan "egasiz" akkauntlar).
- [ ] JML hodisalari: HR'dagi o'zgarish IdM'da hodisa hosil qiladi.

### 3. Rol modeli
- [ ] Biznes rollari: lavozim va bo'lim bo'yicha avtomatik beriladigan rollar.
- [ ] Rol → tizim ruxsatlari (AD guruhlari, ABS rollari).
- [ ] SoD qoidalari (masalan "kredit beruvchi" va "kreditni tasdiqlovchi" bitta odamda bo'lmasin).

### 4. Ariza va tasdiqlash portali
- [ ] Xodim qo'shimcha ruxsat so'raydi, keyin rahbar va resurs egasi tasdiqlaydi.
- [ ] Har bir qadam auditga yoziladi. Workflow dvigateli tanlanadi (Elsa yoki o'zimiz).

### 5. Maqsadli tizimlarga yozish (provisioning)
- [ ] Konnektor interfeysi: har bir tizim alohida modul.
- [ ] AD konnektori: akkaunt yaratish, bloklash, guruhga qo'shish va olib tashlash.
- [ ] Navbat, qayta urinish, xatolar jurnali. Keyin ABS va pochta konnektorlari.

### 6. Attestatsiya va hisobotlar
- [ ] Davriy kampaniyalar: rahbar o'z xodimlarining ruxsatlarini tasdiqlaydi yoki olib tashlaydi.
- [ ] Hisobotlar: kim nimaga kirish huquqiga ega, kim berdi, qachon.

### 7. Xavfsizlik va ekspluatatsiya
- [ ] Standart parollarni almashtirish: `administrator/password`, `SIDS-manager` sirri. Demo klientlarni va `appsettings.json`dagi Facebook/Google sirlarini olib tashlash.
- [ ] Sirlar saqlovchisi (Vault yoki shunga o'xshash), haqiqiy TLS sertifikatlari, admin uchun MFA.
- [ ] Zaif paketlarni yangilash (`System.Linq.Dynamic.Core` 1.3.7, `SQLitePCLRaw` 2.1.6).
- [ ] Audit loglarini SIEM'ga yuborish, bazani zaxiralash, monitoring, yuqori mavjudlik (HA).

### Arxitektura qarori (1-bosqichdan oldin)
SimpleIdServer'ni IdM yadrosi sifatida kengaytiramizmi, yoki uni faqat SSO/provisioning uchun ishlatib,
IdM yadrosini (JML, rollar, arizalar) alohida C# xizmat sifatida yozamizmi? Tavsiya: alohida yadro.
Sabab: SimpleIdServer'da rol modeli, ariza va attestatsiya yo'q, upstream kodga kirib borish esa har bir
yangilanishda patch'larni qayta moslashtirishni talab qiladi.
