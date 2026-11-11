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
- **Bir nechta DC:** `"Servers": ["dc1.bank.uz", "dc2.bank.uz:636"]` — tartib bo'yicha; server javob bermasa (tarmoq, o'chgan) keyingisi,
  noto'g'ri parolda (LDAP 49) darhol to'xtaydi — xizmat akkaunti barcha DC'larda bloklanib qolmasligi uchun. Bo'sh — `Server`.
  Bitta sinxronlash yoki yozish to'plami bitta DC bilan ishlaydi (replikatsiya kechikishi bir to'plam ichida ta'sir qilmaydi).
- **O'qish:** sahifalab (AD MaxPageSize), katta guruhlar `member;range=` bilan, `objectGUID` o'zgarmas kalit, bloklanganlar
  `DisabledFilter` bilan. LDAPS sertifikati Linux'da `LDAPTLS_CACERT` orqali tekshiriladi; `libldap.so.2` kerak.
- **Ichma-ich guruhlar** (`NestedGroups`, standart — ha; faqat `GroupMemberIsDn`): guruh boshqa guruhning a'zosi bo'lsa, xodim
  tashqi guruh huquqini ham oladi — bu **meros** a'zolik (`account_entitlements.via_entitlement_id` — qaysi bevosita guruh orqali,
  eng yaqini). Ko'p darajali va halqali ichma-ichlik xavfsiz. Meros a'zolik "haqiqatda bor", SoD, hisobotlar va arizaning
  "bajarildi" tekshiruvida hisobga olinadi (masalan Domain Admins'ga ichki guruh orqali kirgan xodim imtiyozli ko'rinadi), lekin
  tizimga yozish uni bevosita olib tashlamaydi (ishdan ketganda bevosita guruhlardan chiqarilsa, meros o'zi yo'qoladi) va
  attestatsiyada alohida ko'rib chiqilmaydi — uni bergan bevosita guruh ko'rib chiqiladi. Portalda meros guruh "↳ guruh" bilan.
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
  Filial — `Columns.Branch` (standart sozlanmagan: filial o'zgartirilmaydi). Sozlansa, ustun faylda bo'lishi shart, aks holda import
  rad etiladi (yo'qolgan ustun hammani "boshqa filialga o'tgan" qilib qo'ymasligi uchun). Sinov fayli: `branch` ustuni,
  stendda yoqish — `Idm__Connectors__HR__Csv__Columns__Branch=branch`.
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

### O'tish (mover): boshqa filial yoki bo'limga
`Idm:Mover` — `{ "Enabled": true, "GraceDays": 7, "MaxMovePercent": 20, "IntervalSeconds": 300 }`.
- **Qachon:** HR'da filial (manba bersa) yoki bo'lim o'zgarsa. Bo'shliq va katta-kichik harf farqi o'tish emas. Lavozim yoki rahbar
  o'zgarishi o'tish emas: avtomatik rollar qoidadan o'zi yangilanadi.
- **Nima bo'ladi** (HR importi bilan bitta tranzaksiyada): avtomatik rollar qayta hisoblanadi; ariza va qo'lda berilgan rollarga
  `mover_due_at = hozir + GraceDays` qo'yiladi (shu orada o'zi tugaydigan muddatli rolga — yo'q; takroriy o'tish muddatni uzaytirmaydi);
  xodimning hal qilinmagan arizalari bekor qilinadi (`beneficiary-moved`: tasdiqlash zanjiri eski rahbarniki). `GraceDays=0` — rollar darhol tugaydi.
- **Qaror:** *Arizalar → O'tgan xodimlar*. Xodimning hozirgi (yangi) rahbari, uning amaldagi o'rinbosari yoki administrator: *Qoldirish*
  (asos majburiy, ≥ 5 belgi) yoki *Olib tashlash* (darhol). Xodim o'z roli haqida qaror qilmaydi; qaror bir marta (shartli yangilash).
  Rahbarga pochta xabari (o'rinbosarga ham). Rahbari yo'q xodim — administratorlar ro'yxatida (*Barcha o'tishlar*).
- **Muddat o'tsa:** fon ishi (faqat HA yetakchisida) rolni tugatadi (`mover-grace-expired`); AD guruhlari tizimga yozish orqali olinadi
  (faqat IdM qo'shgan a'zolik — G bosqichi qoidasi). IdM'dan tashqari berilgan eski guruhlar "Ortiqcha" bo'lib, attestatsiyada ko'rinadi.
- **Himoya:** bitta importda `MaxMovePercent` dan ko'p xodim o'tsa — import to'xtatiladi (administrator tasdiqlab qayta ishga tushiradi).
- **Audit:** `identity.role-mover-pending`, `identity.role-mover-kept`, `identity.role-removed` (`reason`: `mover`, `mover-revoked`,
  `mover-grace-expired`), `request.cancelled` (`beneficiary-moved`).
- **Cheklov:** o'tish faqat HR manbali xodimlarda aniqlanadi (AD manbali — HR ulanguncha — aniqlanmaydi).
- API: `GET /api/mover-reviews?scope=mine|all` (`all` — admin, auditor), `POST /api/mover-reviews/{id}` `{ "decision": "keep"|"revoke", "comment" }`.

## Ikki kishilik tasdiq ("to'rt ko'z")
`Idm:FourEyes` — `{ "Enabled": true, "ExpiryHours": 72 }`; production'da o'chirib bo'lmaydi (ProductionGuard).
- **Qaysi amallar:** portal vakolatini berish/olish (*Nazorat → Portal vakolatlari*), rol ruxsatlarini o'zgartirish, avtomatik qoida
  qo'shish/olib tashlash, rolni o'chirish, SoD qoidasini yaratish/o'chirish, SoD istisnosini berish, ommaviy o'chirishni tasdiqlab
  sinxronlash. Rolni yaratish, tahrirlash, SoD istisnosini bekor qilish, attestatsiya va boshqalar — darhol (nazoratni kuchaytiradi yoki kam xavfli).
- **Qanday:** amal xuddi shu kod bilan bekor qilinadigan tranzaksiyada sinab ko'riladi — xato bo'lsa darhol qaytadi, aks holda so'rov
  saqlanadi (HTTP 202, `changeRequest`) va unda aynan nima o'zgarishi (audit yozuvlari) ko'rinadi. Boshqa administrator *Nazorat → Tasdiqlar*da
  tasdiqlasa, amal so'rovchi nomidan qayta tekshirilib bajariladi (`change.approved` bilan bitta tranzaksiyada); shu orada sharoit o'zgargan
  bo'lsa — hech narsa o'zgarmaydi, so'rov "Bajarib bo'lmadi". Rad etish — sabab bilan; bekor qilish — faqat so'rovchi.
- **Taqiqlar:** o'z so'rovini tasdiqlash; o'ziga tegishli o'zgarishni (o'ziga vakolat, o'ziga SoD istisnosi) tasdiqlash; administratorlikdan
  chiqarilgan xodimning so'rovini bajarish; ikkitadan kam administrator qoldirish. Holat o'tishlari shartli yangilash bilan (ikki bosish — bir natija).
- Boshqa administratorlarga pochta xabari; menyuda kutilayotganlar soni. Audit: `change.requested`, `change.approved`, `change.rejected`,
  `change.cancelled`, `change.failed`, `portal-role.granted`, `portal-role.revoked`.
- API: `GET /api/change-requests[?status=pending]`, `POST /api/change-requests/{id}/approve|reject|cancel`, `GET /api/portal-roles`,
  `POST /api/portal-roles`, `POST /api/portal-roles/revoke`.

## Rollar va SoD (E bosqichi)
- **Rol** — biznes roli: tizimlardagi ruxsatlar (AD guruhlari) to'plami. Kod, nom, xavf darajasi, ariza orqali so'rash mumkinligi.
- **Avtomatik ("tug'ma") qoida:** filial, bo'lim va/yoki lavozim (bo'sh — "har qanday", hammasi bo'sh — taqiqlangan). Qoidaga mos
  ishlayotgan (va ishga kirishi kutilayotgan) xodimga rol beriladi, mos kelmay qolsa yoki ishdan ketsa — olib tashlanadi.
  Ishdan ketganda ariza/qo'lda berilgan rollar ham tugaydi — muddatli (tugash sanasi kelajakda) rollar ham.
  Qayta hisoblash har muvaffaqiyatli sinxronlashdan keyin avtomatik va "Qayta hisoblash" tugmasi bilan; qoida qo'shilganda avval
  "N ta xodimga mos" ko'rinadi. Faqat avtomatik tayinlashlarga tegadi; olib tashlangan rol o'chirilmaydi (`valid_to`), har o'zgarish auditda.
  Parallel hisoblashdan advisory lock va `identity_roles` dagi noyob indeks himoya qiladi.
- **"Bo'lishi kerak / haqiqatda bor":** rollardan kelgan ruxsatlar va tizimdan o'qilgan a'zolik solishtiriladi: *Bor*, *Yetishmaydi*
  (tizimga yozish bilan beriladi), *Ortiqcha* (rol bilan asoslanmagan — attestatsiyada rahbar ko'rib chiqadi). Xodim kartochkasi va "Ruxsatlar farqi" hisoboti.
- **SoD:** qoida — ikki tomon ruxsatlari (A va B); xodimda haqiqatda ikkala tomondan ruxsat bo'lsa — buzilish. Rolning o'zi
  qoidani buzsa, rol sahifasida ogohlantirish. Buzilishlar bosh sahifa, menyu va xodim kartochkasida ko'rinadi.
- **SoD istisnosi** (*SoD → Buzilishlar → Istisno berish*, faqat administrator): aniq xodim va qoida uchun, asos va kompensatsion nazorat
  majburiy (10–2000 belgi), oxirgi kun — ertadan bir yilgacha (Toshkent vaqti, oxirgi kun to'liq kiradi). O'ziga istisno berib
  bo'lmaydi; bir xodim va qoida uchun bitta amaldagi istisno; o'zgartirilmaydi (yangi muddat — yangi istisno). Istisnoli buzilish
  ro'yxatlarda "Istisno · sana gacha" bilan ko'rinadi, lekin ochiq buzilishlar soniga (bosh sahifa, qoida) kirmaydi; muddat tugasa
  yoki bekor qilinsa — yana ochiq. Hisobotda istisno va nazorat ustunlari. Audit: `sod.exception-granted`, `sod.exception-revoked`.
  API: `GET /api/sod/exceptions[?all=true]` (admin, auditor), `POST /api/sod/exceptions`, `POST /api/sod/exceptions/{id}/revoke` (admin).
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
- **Rol egasi** — rol formasida (*Rollar → Tahrirlash*) xodim qidirib belgilanadi (ishdan ketgan bo'lmasligi kerak); bo'sh — rol egasi
  bosqichi administratorlar navbatiga tushadi. O'zgarish `role.updated` auditida (oldingi va yangi egasi tabel raqami bilan).
- **O'rinbosar** (*Arizalar → O'rinbosar*): xodim o'zi (administrator — istalgan xodim uchun, `delegatorId`) muddatli o'rinbosar
  tayinlaydi — bugundan, ko'pi bilan 180 kun, kunlar to'liq (Toshkent vaqti). Muddat davomida o'rinbosar shu xodimga tushgan ariza
  bosqichlari va attestatsiya yozuvlarini ko'radi va hal qiladi. Zanjir yo'q (o'rinbosarning o'rinbosari ishlamaydi), ishdan ketgan
  xodim o'rniga qaror qilinmaydi, o'z arizasi va o'z ruxsatini o'rinbosar ham tasdiqlay olmaydi. Bir xodimda bir davrda bitta
  o'rinbosarlik. Auditda qaror o'rinbosar nomidan, `onBehalfOf` (kimning o'rniga) bilan; `delegation.created`, `delegation.revoked`.
  Hamkasb tanlash — `GET /api/directory?search=` (faqat ishlayotganlar, ism/tabel/bo'lim, eng ko'pi 10 ta).
  API: `GET/POST /api/me/delegations`, `POST /api/delegations/{id}/revoke` (tayinlagan yoki admin), `GET /api/delegations` (admin, auditor).
- **Ishdan ketganda** barcha rollar (ariza va qo'lda berilganlar ham) tugaydi, kutilayotgan arizalar bekor qilinadi.

## Tizimga yozish (G bosqichi)
Rejalashtiruvchi "bo'lishi kerak" (rollar, xodim holati) va tizimdan o'qilgan holatni solishtirib amallar navbatini tuzadi
(`provisioning_operations`), ishlovchi ularni tizimda bajaradi. Standart — o'chiq; har tizim uchun alohida yoqiladi:
```json
"AD": { "Provisioning": { "Mode": "Approval", "CreateAccounts": false, "MaxAutoOperations": 20, "ProtectedLogins": [ "administrator" ],
                          "DisableSuspended": true, "MaxAttempts": 5, "EnableReturning": true, "EnableNewAccounts": false },
        "Ldap": { "WriteBindDn": "CN=svc-idm-write,...", "WriteBindPassword": "<sir>", "CreateDn": "OU=Yangi,OU=Xodimlar,DC=bank,DC=uz",
                  "UpnSuffix": "bank.uz" } }
```
- **Rejimlar:** `Off`; `Approval` — dry-run: amallar "Tasdiq kutmoqda", administrator portalda (*Tizimga yozish*) tanlab tasdiqlaydi;
  `Automatic` — darhol navbatga, lekin bitta rejada `MaxAutoOperations`dan ko'p yangi amal chiqsa hammasi tasdiqqa to'xtatiladi
  (`provisioning.held`) — noto'g'ri HR fayli yoki rol butun bankni bloklab yubormasligi uchun.
- **Nima qilinadi:** ishdan ketgan xodim — barcha akkauntlari bloklanadi va barcha guruhlardan chiqariladi; to'xtatilgan — bloklanadi;
  rol bo'yicha kerak guruh — shaxsiy akkauntga qo'shiladi; akkaunt yo'q bo'lsa (`CreateAccounts`) — **bloklangan** yaratiladi
  (login `i.familiya`, band bo'lsa raqam bilan). AD'da yaratishda **tasodifiy 40 belgili parol** qo'yiladi: u hech qayerda saqlanmaydi,
  logga yozilmaydi va hech kimga ma'lum emas (IdM'da parol yo'q), xodimga parolni helpdesk beradi. Sabab — jonli sinovda Samba AD
  parolsiz akkauntni yoqishga ruxsat berdi (`pwdLastSet: 0`, `userAccountControl: 512`): katalog sozlamasiga qarab akkaunt bo'sh parol
  bilan faol bo'lib qolishi mumkin edi. Rol olib tashlansa —
  **faqat IdM o'zi qo'shgan** a'zolik olinadi. Qo'lda berilgan ortiqcha ruxsatlarga tegilmaydi (H bosqichida attestatsiya).
- **Qayta yoqish:** qayta ishga olingan (HR'da yana ishlayapti) yoki to'xtatilishi tugagan xodimning akkaunti yoqiladi
  (`EnableReturning`, standart — ha), lekin **faqat IdM o'zi bloklagan bo'lsa**: akkaunt bo'yicha IdM'ning oxirgi muvaffaqiyatli
  holat amali "ishdan ketgani/to'xtatilgani uchun bloklash" bo'lishi kerak. Administrator qo'lda bloklagan akkauntga tegilmaydi.
  Tartib: avval guruhlar qo'shiladi, keyin akkaunt yoqiladi; tasdiqdan keyin xodim yana ketsa yoki to'xtatilsa — amal "Keraksiz".
  IdM yaratgan (bloklangan) akkauntni ishga kirish kuni yoqish — `EnableNewAccounts` (standart — yo'q): akkauntning paroli tasodifiy
  va noma'lum, shuning uchun yoqilgan akkauntga helpdesk parol bermaguncha hech kim kira olmaydi. Katalog yoqishni rad etsa (parol
  siyosati), amal "Xato" bo'ladi va xabarda "avval helpdesk parol o'rnatsin" deyiladi. Cheklov: IdM bloklagandan keyin akkauntni kimdir qo'lda yoqib,
  keyin yana qo'lda bloklagan bo'lsa, buni IdM ajrata olmaydi (Approval rejimida administrator ko'radi).
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

### Bank AD'sida yozish akkaunti huquqlari (sinovda tekshirilgan)
Samba AD DC'da (`AdLiveTests`) `svc-idm-write` faqat quyidagi topshirilgan huquqlar bilan barcha IdM amallarini bajardi
(yaratish, bloklash, yoqish, guruh a'zoligi), boshqa xodimning parolini almashtira olmadi:
| Obyekt | ACE (SDDL) | Ma'nosi |
|---|---|---|
| `OU=Xodimlar` (`UsersDn`/`CreateDn`) | `(OA;CI;CC;bf967aba-0de6-11d0-a285-00aa003049e2;;<SID>)` | faqat `user` obyekti yaratish |
| `OU=Xodimlar` | `(OA;CIIO;RPWP;;bf967aba-0de6-11d0-a285-00aa003049e2;<SID>)` | `user` obyektlari atributlarini o'qish/yozish (`userAccountControl`) |
| `OU=Guruhlar` (`GroupsDn`) | `(OA;CIIO;RPWP;bf9679c0-0de6-11d0-a285-00aa003049e2;bf967a9c-0de6-11d0-a285-00aa003049e2;<SID>)` | faqat guruhlarning `member` atributi |
O'chirish, parolni almashtirish (Reset Password), boshqa OU — berilmaydi. Windows'da: *Delegate Control* bilan shu uchtasi.
O'qish akkaunti (`svc-idm`) — oddiy domen foydalanuvchisi. Jonli testlar: `IDM_TEST_AD=ldaps://dc:636 IDM_TEST_AD_PASSWORD=...
IDM_TEST_AD_WRITE_DN=... IDM_TEST_AD_WRITE_PASSWORD=... IDM_TEST_AD_ADMIN_DN=... IDM_TEST_AD_ADMIN_PASSWORD=... LDAPTLS_CACERT=ca.pem
dotnet test --filter-class '*AdLiveTests'` (helpdesk va tozalash — admin akkaunti; bank AD'sida alohida sinov OU'sida bajaring).

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
- Yakunlashda ko'rib chiqilmagan yozuvlar shundayligicha qoladi va hisobotda "ko'rib chiqilmagan" deb ko'rinadi — agar kampaniya
  **"Muddatida ko'rib chiqilmaganlarni olib tashlash"** siyosati bilan yaratilmagan bo'lsa (`revokeUnreviewed`, standart — yo'q).
  Siyosat bilan: oxirgi kun (Toshkent) tugagach yetakchi server kampaniyani o'zi yakunlaydi (yoki administrator yakunlaganda),
  ko'rib chiqilmagan yozuvlar tizim nomidan "olib tashlandi" (`review.revoked`, `automatic: true`) — rol darhol tugaydi, guruh tizimga
  yozish orqali olinadi. Bekor qilingan kampaniyaga siyosat qo'llanmaydi. Rahbar ta'tilda bo'lsa — o'rinbosar ko'rib chiqadi (F bosqichi).
- **Rol tarkibi attestatsiyasi** (*Attestatsiya → Rol tarkibi*, faqat rol egalari va ularning o'rinbosarlari; egasiz rollar —
  administratorlar): egasi har bir ruxsat hamon kerakligini tasdiqlaydi, keraksizini belgidan chiqaradi (sabab majburiy) — ruxsat roldan
  olinadi va rolning barcha xodimlaridan tizimga yozish orqali olinadi (IdM qo'shgan a'zoliklar). Ega faqat olib tashlay oladi, qo'shish —
  administrator. Muddat: `Idm:RoleCertificationDays` (standart 365; hech qachon tasdiqlanmagan yoki muddati o'tgan rol menyuda hisoblanadi).
  Audit: `role.certified` (olib tashlanganlar, izoh, `onBehalfOf`). API: `GET /api/me/owned-roles`, `POST /api/roles/{id}/certify`.
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

## Ekspluatatsiya (`deploy/`)
- `db-roles.sql` — PostgreSQL rollari: `idm_owner` (migratsiyalar), `idm_app` (API; audit jurnaliga faqat qo'shish), `idm_reader` (o'qish).
- `backup.sh` — `pg_dump` (izchil snapshot), ixtiyoriy GPG shifrlash, manifest (SHA-256, oxirgi audit yozuvi va xeshi), saqlash muddati.
- `restore-verify.sh` — nusxani vaqtinchalik bazaga tiklab tekshiradi: nazorat summasi, audit belgisi, butun xesh zanjiri; keyin bazani o'chiradi.
- `dotnet Bank.Idm.Api.dll verify-audit` — istalgan bazada audit zanjiri (ulanish `ConnectionStrings__Idm` muhitidan); kod 0/2/1.
- Xavfsizlik nazoratlari, pentest va chiqarish ro'yxatlari: [XAVFSIZLIK.md](XAVFSIZLIK.md).

## Yuklama (bank miqyosi)
`IDM_LOAD_TEST=10000 dotnet test --filter-class '*LoadTests'` — xodimlar, akkauntlar (o'rtacha 6 guruh), 300 guruh, 40 avtomatik rol,
20 SoD qoidasi; har bir amal vaqt chegarasi bilan tekshiriladi (CI'da emas). Mahalliy kompyuterda (PostgreSQL 18), soniyada:

| Amal | 10 000 xodim | 50 000 xodim |
|---|---|---|
| HR birinchi yuklash | 1.7 | 4.6 |
| AD birinchi yuklash (akkaunt, guruh, a'zolik) | 3.6 | 13.4 |
| AD qayta sinxronlash (o'zgarishsiz) | 1.3 | 5.1 |
| Avtomatik rollar (birinchi / o'zgarishsiz) | 0.9 / 0.1 | 4.4 / 0.3 |
| Tizimga yozish rejasi (birinchi: 30 ming / 151 ming amal; takror) | 2.8 / 0.4 | 12.6 / 1.4 |
| "Kirish huquqlari" hisoboti + Excel (60 ming / 300 ming qator) | 0.2 + 0.3 | 1.2 + 1.3 |
| Butun bank attestatsiya kampaniyasi (58 ming / 289 ming yozuv) | 1.1 | 5.8 |

Yuklama testi topgan va tuzatilgan: kampaniya yozuvlari EF o'rniga PostgreSQL `COPY` bilan (50 ming xodimda 60 s → 6 s — HTTP
so'rovi proksi vaqt chegarasiga yetmaydi); Excel varag'i oqim bilan yoziladi (ajratilgan xotira 1.7 GB → 240 MB).
Hisobot chegaralari: Excel — 1 000 000 qator, chop etish (brauzer) — 20 000 qator (undan katta bo'lsa 413 va Excel taklifi).

**Bir vaqtda ko'p foydalanuvchi** (k6, API ustida): [tests/load/README.md](tests/load/README.md).

## So'rovlar chegarasi va vaqt chegarasi
Bitta foydalanuvchi, skript yoki og'ir hisobot serverni band qilib qo'ymasligi uchun (`Idm:Limits`, standart qiymatlar bilan yoqilgan):

| Sozlama | Standart | Ma'nosi |
|---|---|---|
| `UserBurst` / `UserPerSecond` | 200 / 20 | Login bo'yicha: birdaniga 200 so'rov, uzoq muddatda soniyasiga 20 (portal sahifasi 5–15 so'rov) |
| `AnonymousPerMinute` | 120 | Tokensiz yoki yaroqsiz tokenli so'rovlar, IP bo'yicha (`TrustedProxies` orqali haqiqiy IP) |
| `HeavyPerUser` | 1 | Og'ir amal bitta foydalanuvchidan bir vaqtda (takror bosish) |
| `HeavyConcurrent` / `HeavyQueue` | 4 / 16 | Og'ir amallar butun nusxa bo'yicha; ortig'i navbatda kutadi, navbat to'lsa 429 |
| `RequestTimeoutSeconds` / `HeavyTimeoutSeconds` | 30 / 300 | Oshsa 504, bazadagi so'rov ham bekor qilinadi |

Og'ir amallar: hisobot eksporti, audit zanjirini tekshirish, rollarni qayta hisoblash, tizimga yozish rejasi, kampaniya (oldindan
ko'rish va yaratish), qo'lda sinxronlash. Rad etilganda — 429, `Retry-After` va tushunarli xabar (portal tanlangan tilda ko'rsatadi);
har biri logda va `idm_http_throttled_total{reason="user|anonymous|heavy"}` metrikasida. `/health` va `/metrics` cheklanmaydi.
Chegaralar har nusxada alohida. Proksi (nginx/HAProxy) vaqt chegarasi `HeavyTimeoutSeconds`dan katta bo'lishi kerak.

**Javob hajmi va kesh** (k6 topilmalari, 2026-11-06):
- JSON javoblar brotli/gzip bilan siqiladi (`Idm:ResponseCompression`, standart yoqilgan; proksi siqsa o'chirish mumkin). Excel siqilmaydi.
- Attestatsiya navbati (`/api/reviews/inbox?skip=&take=`) xodimlar bo'yicha sahifalanadi: bir sahifada 25 xodim va ularning barcha
  yozuvlari. Ilgari bitta so'rov 500 ta yozuv (≈320 KB) qaytarardi.
- Bosh sahifa hisoblari (SoD buzilishlari ham) `Idm:DashboardCacheSeconds` (30 s) davomida umumiy keshdan olinadi. Bir vaqtda kelgan
  so'rovlardan faqat bittasi qayta hisoblaydi. Sinxronlash tugasa, kesh darhol yangilanadi. Tizimlar holati keshsiz, jonli.

## Xabarnomalar (pochta)
`Idm:Notifications` (standart — o'chiq). Faqat yetakchi server rejalashtiradi va yuboradi; har bir hodisa uchun bitta xat (navbat
`notifications`, noyob kalit — server qayta ishga tushsa ham takrorlanmaydi). Xat o'zbekcha va ruscha, portal havolasi bilan;
asos, izoh va ruxsatlar ro'yxati xatga kirmaydi (faqat portalda). Emaili yo'q yoki ishdan ketgan xodimga yozilmaydi.
```json
"Notifications": { "Enabled": true, "PortalUrl": "https://idm.bank.uz", "ReminderAfterHours": 24, "MaxReminders": 3, "ReviewDueReminderDays": 3,
                   "Smtp": { "Host": "smtp.bank.uz", "Port": 587, "UseTls": true, "User": "svc-idm-mail", "Password": "<sir>", "From": "idm@bank.uz" } }
```
- **Ariza:** navbatdagi bosqich egasiga (va uning amaldagi o'rinbosariga) — yangi bosqich; hal qilinmasa har `ReminderAfterHours`da eslatma
  (`MaxReminders` martagacha); tasdiqlansa yoki rad etilsa — so'ragan va kim uchun so'ralgan xodimga natija. Administratorlar navbati — portalda.
- **Attestatsiya:** kampaniya boshlanganda ko'rib chiqilmagan yozuvlari bor har bir tekshiruvchiga (va o'rinbosariga) bitta xat;
  muddatdan `ReviewDueReminderDays` kun oldin — eslatma.
- **Yetkazish:** SMTP STARTTLS (587; ishlab chiqarishda `UseTls=false` va HTTP `PortalUrl` taqiqlangan, parol — sirlar orqali).
  Xatoda 1, 5, 15, 60 daqiqadan keyin qayta urinish, `MaxAttempts` (5) dan keyin "yuborilmadi". Metrika: `idm_notifications{status}`.
  Birinchi yoqilganda faqat kutilayotgan bosqichlar va so'nggi 2 kunda yopilgan arizalar haqida yoziladi.

## Portal tillari
O'zbekcha (standart) va ruscha: shapkadagi **RU / UZ** tugmasi, tanlov brauzerda (`localStorage`, `idm.lang`) saqlanadi va sahifa
qayta yuklanadi. Matnlar `portal/src/i18n/uz.ts` (asosiy) va `ru.ts` — kalitlar va funksiya imzolari bir xil ekanini TypeScript va
test tekshiradi; audit amallari nomlari — `i18n/index.ts` (`actionsRu`, test yangi amalning tarjimasi unutilmasligini tekshiradi).
API xato xabarlari (validatsiya, 403/409 matnlari) hozircha o'zbekcha.

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
