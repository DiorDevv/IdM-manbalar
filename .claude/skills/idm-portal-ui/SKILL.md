---
name: idm-portal-ui
description: Bank IdM portali (idm/portal, React + TypeScript) dizayn tizimi va UI qoidalari — Linear uslubidagi zamonaviy, och/tungi mavzuli interfeys. Portalda har qanday sahifa, komponent, jadval, forma, modal, ikonka, rang yoki matn qo'shishda yoki o'zgartirishda shu skill'dan foydalaning — hatto foydalanuvchi "dizayn" so'zini aytmasa ham (masalan "rollar sahifasini qo'sh", "arizalar ro'yxatini chiqar", "tugma qo'y", "UI'ni chiroyli qil", "portal zerikarli ko'rinyapti").
---

# Bank IdM portali: dizayn tizimi

Portal — bank xodimlari, rahbarlar, auditorlar va IdM administratorlari ishlatadigan ichki vosita. Uslub: **Linear / Vercel / Stripe** kabi
ixcham, havodor, ingichka chiziqli, tez. Maqsad — ko'p ma'lumotni charchatmasdan ko'rsatish va xavfli holatlarni (ishdan ketgan xodimning
faol akkaunti, imtiyozli guruh) darhol ko'zga tashlash. Bezak uchun bezak yo'q: har bir rang va ikonka ma'no bildiradi.

## Fayllar

| Fayl | Nima |
|---|---|
| `idm/portal/src/styles.css` | Barcha tokenlar (`:root`, tungi mavzu) va komponent klasslari. Yangi rang yoki o'lcham faqat shu yerda token sifatida |
| `idm/portal/src/components/ui.tsx` | Umumiy komponentlar: `PageHead`, `Stat` (`tone`), `Empty`, `ErrorBox`, `SkeletonRows`, `StatusBadge`, `RiskBadge`, `RunStatusBadge`, `RunSummary`, `GroupChips`, `Pager`, `Tabs`, `ConfirmDialog`, `Avatar`, `Person`, `ActivityFeed` |
| `idm/portal/src/i18n/index.ts` | `describeAction` (audit amali → nom, ikonka, rang, xulosa), `isQuietSync`, sana formatlari |
| `idm/portal/src/components/Icon.tsx` | Ichki SVG ikonkalar (chiziqli, 1.75 qalinlik). Tashqi ikonka kutubxonasi yo'q |
| `idm/portal/src/components/Layout.tsx` | Yon panel, yuqori panel, mavzu tugmasi, mobil menyu |
| `idm/portal/src/components/CommandPalette.tsx` | ⌘K / Ctrl+K: sahifalarga o'tish va xodim qidirish |
| `idm/portal/src/theme.ts` | Och / tungi / tizim mavzusi (`data-theme`, localStorage) |
| `idm/portal/src/i18n/uz.ts` | Barcha matnlar. Komponent ichida o'zbekcha matn yozilmaydi |

## Qat'iy cheklovlar (bank xavfsizligi)

- **Tashqi manba yo'q:** CDN, Google Fonts, tashqi rasm yoki skript ishlatilmaydi. Shrift — `@fontsource-variable/inter` (npm, loyiha ichida).
  Sabab: bank tarmog'i internetdan ajratilgan bo'lishi mumkin va tashqi so'rov ma'lumot chiqib ketish kanali.
- **Yangi npm paket** — faqat zarur bo'lsa, aniq versiya (`--save-exact`), kamida 2 hafta oldin chiqqan. UI kutubxonalari (MUI, Ant, Tailwind)
  qo'shilmaydi: dizayn tizimi o'zimizniki va kichik.
- **`dangerouslySetInnerHTML` yo'q.** Serverdan kelgan matn (audit tafsilotlari, xato xabarlari) faqat matn sifatida chiqadi.
- Brauzer dialoglari (`alert`, `confirm`) o'rniga sahifa ichidagi tasdiqlash oynasi (`ConfirmDialog`) — xavfli amal (o'chirish, ommaviy
  bloklash) nima bo'lishini aniq yozadi.

## Tokenlar

Ranglar to'g'ridan-to'g'ri yozilmaydi (`#1f56d1` emas, `var(--accent)`): shunda tungi mavzu avtomatik ishlaydi.

| Token | Ma'nosi |
|---|---|
| `--bg`, `--panel`, `--panel-2`, `--sidebar` | Sahifa foni, karta, ikkinchi darajali fon (jadval sarlavhasi, hover), yon panel |
| `--text`, `--text-2`, `--muted`, `--faint` | Asosiy matn → ikkinchi darajali → izoh → eng och (placeholder) |
| `--border`, `--border-strong` | Ingichka chiziqlar (1px). Soya o'rniga asosan chiziq |
| `--accent`, `--accent-hover`, `--accent-soft`, `--accent-text` | Bank ko'k rangi: asosiy tugma, faol menyu, havola, fokus |
| `--ok`, `--warn`, `--bad` (+ `-soft`) | Holatlar: faol / e'tibor / xavf. Faqat ma'no bilan ishlatiladi |
| `--radius-sm` 6px, `--radius` 10px, `--radius-lg` 14px | Badge va input / tugma va karta / modal |
| `--shadow-sm`, `--shadow-pop` | Karta uchun deyarli ko'rinmas; modal va menyu uchun aniq |

**Tipografiya:** Inter Variable. Asosiy matn 14px, jadval 13.5px, izoh 12.5px, sahifa sarlavhasi 22px / 650, karta sarlavhasi 14px / 600.
Raqamlar `font-variant-numeric: tabular-nums` (`.num`). Tabel raqami, login, ID — `.mono`.

**Oraliq:** 4px qadam (4, 8, 12, 16, 20, 24, 32). Kontent eni ≤ 1320px, chetlari 32px (mobil 16px).

## Vizual til (zamonaviylik detallari)

Oddiy "oq karta + jadval" yetarli emas — har sahifada ierarxiya, rang ma'nosi va hayot bo'lishi kerak:
- **Nur va chuqurlik:** kontent tepasida yumshoq ko'k/binafsha nur (`.main-col::before`), hero blokda nuqtali naqsh. Bezak — faqat fon, matn ustida emas.
- **Ton ranglari:** `accent` (ko'k), `violet`, `teal`, `sky`, `warn`, `bad`, `ok` — `data-tone` yoki `Stat tone`. KPI kartalarida har bir ko'rsatkich o'z tonida;
  ton faqat ma'no bilan (xavf — `bad`, e'tibor — `warn`, HR — `teal`).
- **Odamlar har doim avatar bilan** (`Person`/`Avatar`): rang ismdan hisoblanadi, bir xodim hamma joyda bir xil rangda.
- **Audit — odam tilida:** `ActivityFeed` / `describeAction`: ikonka, rang, "Akkaunt bloklandi · AD · a.karimov". Texnik kod (`account.disabled`) faqat ikkinchi qatorda.
  O'zgarishsiz jadval sinxronlashlari bosh sahifa lentasida ko'rsatilmaydi (`isQuietSync`).
- **Xavf ko'zga tashlanadi:** yon panel hisoblagichi (Korrelyatsiya), qo'ng'iroqdagi qizil nuqta, hero'dagi "insight" pill'lar, xodim kartochkasida qizil ogohlantirish.
- **Grafiklar** (`dataviz` skill qoidalari): bitta o'q, yupqa belgilar, rang yolg'iz emas — legenda nom va son bilan, `title`/`aria-label`.
- **Harakat:** hover'da kartaning 1px ko'tarilishi, jonli holatda pulsatsiya (`.badge.live`, `.health.live`), sahifaga kirishda 180ms paydo bo'lish.

## Komponent qoidalari

- **Sahifa:** `PageHead` (sarlavha, bir qatorli izoh, o'ngda amallar) → statistika (`.grid.stats` + `Stat`) → asosiy `card`. Breadcrumb
  ichki sahifalarda (`crumbs`).
- **Stat:** ikonka chipi, katta raqam, kichik izoh. `attention` — faqat haqiqiy xavf bo'lsa (raqam > 0). Bosilsa tegishli ro'yxatga olib boradi.
- **Jadval** (`table.data`): sarlavha kichik va och, qator balandligi ~44px, hover foni. Asosiy ustun: `.cell-main` + `.cell-sub`. Raqamli
  ustun o'ngga tekislangan va `.num`. Bosiladigan qator — `.clickable` va ichida haqiqiy `Link` (klaviatura uchun).
- **Holat badge'i:** rang + nuqta + matn (faqat rang emas — rang ko'r foydalanuvchi uchun). `ok` faol, `warn` kutilmoqda/to'xtatilgan,
  `bad` ishdan ketgan/xato, `neutral` o'chirilgan, `info` jarayonda.
- **Imtiyozli narsa** (Domain Admins va h.k.): `.chip.privileged` + `lock` ikonka. Xavfli holat: `--bad`.
- **Tugmalar:** `btn primary` (sahifada bitta asosiy amal), `btn secondary`, `btn ghost`, `btn danger`. Balandligi 34px. Ikonka + matn.
  Jarayonda — tugma o'chiq va matni o'zgaradi ("Sinxronlanmoqda…").
- **Tablar:** `Tabs` komponenti (`role="tablist"`), soni bilan. Tanlangan tab URL'da (`?tab=`) — havolani ulashish mumkin.
- **Har bir ma'lumot bloki 4 holatda:** yuklanmoqda (`SkeletonRows`/skeleton), xato (`ErrorBox` + "Qayta urinish"; 403 → "Ruxsat yo'q"),
  bo'sh (`Empty`: nima uchun bo'shligi va keyin nima bo'lishi), ma'lumot.
- **Ikonkalar:** `Icon` (16–18px matn yonida, 20px menyuda). Yangi ikonka — `Icon.tsx`ga 24×24 chiziqli path; dekorativ ikonka `aria-hidden`.

## Mavzu va moslashuvchanlik

- Och va tungi mavzu `theme.ts` orqali (`data-theme` = `light` | `dark`; tanlanmagan bo'lsa tizim sozlamasi). Har yangi rang ikkala mavzuda
  tekshiriladi: tungi mavzuda `--*-soft` fonlar shaffof variantda.
- ≤ 1000px: yon panel yashirinadi, yuqori paneldagi menyu tugmasi uni ochadi. Jadvallar gorizontal aylanadi (`.table-wrap`), sahifa emas.

## Kirish imkoniyati (a11y)

- Har bir interaktiv element klaviaturada ishlaydi va ko'rinadigan fokusga ega (`:focus-visible` — `--accent` halqa).
- Matn kontrasti ≥ 4.5:1 (`--muted` ham). Ikonka-tugmada `aria-label`. Modal: `role="dialog"`, `aria-modal`, Esc yopadi, fokus ichida qoladi.
- `prefers-reduced-motion` bo'lsa animatsiya o'chadi.

## Yangi sahifa qo'shish tartibi

1. Matnlar `i18n/uz.ts`ga (bo'lim nomi bilan). API turlari `api/types.ts`ga (backend `Contracts.cs` bilan bir xil).
2. Sahifa `pages/`da: mavjud komponentlardan yig'iladi; yangi uslub kerak bo'lsa — avval `styles.css`ga token/klass.
3. Marshrut `main.tsx`, menyu `Layout.tsx` (`NAV` ro'yxati — u ⌘K palitrasida ham ko'rinadi).
4. Tekshirish: `npm run typecheck && npm test && npm run build` va brauzerda ikkala mavzu, 1440px va 390px enlikda.
