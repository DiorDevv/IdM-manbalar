#!/usr/bin/env bash
# IdServer login sahifasini bank uchun sozlaydi (PostgreSQL bazasida). Qayta ishga tushirish xavfsiz.
#   - o'zbek tili (standart), ingliz tili qoladi, frantsuz tili ro'yxatdan olinadi;
#   - login formasi va xato xabarlariga o'zbekcha tarjimalar;
#   - formadan "Register", "Forgot my password" va Facebook/Google olib tashlanadi;
#   - o'zini o'zi ro'yxatdan o'tkazish server tomonda ham yopiladi (bankda akkauntni faqat AD/HR yaratadi).
# Chiqish kodi 10: tillar ro'yxati o'zgardi, IdServer qayta ishga tushirilishi kerak (start-all.sh buni o'zi qiladi).
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(dirname "$HERE")
. "$ROOT/.secrets/postgres.env"
psql_q() { docker exec -i bank-idm-postgres psql -v ON_ERROR_STOP=1 -U "$PG_USER" -d "$PG_DB" -qtA "$@"; }

before=$(psql_q -c "select string_agg(\"Code\", ',' order by \"Code\") from \"Languages\"")
FORM_JSON=$(psql_q -c "select json_build_object('id', \"Id\", 'elements', \"Elements\", 'errors', \"ErrorMessageTranslations\")
  from \"Forms\" where \"Name\" = 'pwd' and \"Category\" = 'auth' and \"Realm\" = 'master' order by \"VersionNumber\" desc limit 1")

python3 -I - "$FORM_JSON" <<'EOF' | psql_q
import json, sys
form = json.loads(sys.argv[1])
elements = json.loads(form['elements'])
errors = json.loads(form['errors'] or '[]')

# Login formasi: o'zbekcha yorliqlar (element nomi yoki turi bo'yicha)
labels = {'Login': 'Login', 'Password': 'Parol', 'RememberLogin': 'Meni eslab qol', 'Button': 'Kirish', 'Divider': 'YOKI'}
def label(e):
    key = e.get('Name') if e.get('Name') in labels else e.get('Type')
    if key not in labels: return
    lst = [l for l in (e.get('Labels') or []) if l.get('Language') != 'uz']
    lst.append({'Language': 'uz', 'Translation': labels[key]})
    e['Labels'] = lst

# Bank uchun keraksiz: ro'yxatdan o'tish, IdM paroli tiklash (AD parolini o'zgartirmaydi), tashqi provayderlar
removed = []
def clean(e):
    label(e)
    kept = []
    for c in e.get('Elements') or []:
        if c.get('Type') in ('FormAnchor', 'Divider', 'ListData'):
            removed.append(c['Id']); continue
        kept.append(clean(c))
    if 'Elements' in e: e['Elements'] = kept
    return e
elements = [clean(e) for e in elements]

uz_errors = {
    'MaximumNumberActiveSessions': "Faol sessiyalar soni chegarasiga yetildi.",
    'MissingLogin': "Loginni kiriting",
    'InvalidCaptcha': "Captcha noto'g'ri",
    'MissingPassword': "Parolni kiriting",
    'MissingReturnUrl': "Qaytish manzili ko'rsatilmagan",
    'UserDoesntExist': "Login yoki parol noto'g'ri",       # foydalanuvchi borligini oshkor qilmaslik uchun umumiy matn
    'InvalidCredential': "Login yoki parol noto'g'ri",
    'UserBlocked': "Hisobingiz bloklangan. IT yordam xizmatiga murojaat qiling.",
}
# Inglizcha xabar ham login mavjudligini oshkor qilmasin (user enumeration)
for e in errors:
    if e.get('Language') == 'en' and e.get('Code') == 'UserDoesntExist': e['Value'] = 'Invalid login or password'
errors = [e for e in errors if e.get('Language') != 'uz']
for e in [e for e in errors if e.get('Language') == 'en']:
    errors.append({'Language': 'uz', 'Code': e['Code'], 'Value': uz_errors.get(e['Code'], e['Value']), 'Key': f"uz:{e['Code']}"})

def lit(s): return "$j$" + s + "$j$"
print(f'update "Forms" set "Elements" = {lit(json.dumps(elements, ensure_ascii=False))}, "ErrorMessageTranslations" = {lit(json.dumps(errors, ensure_ascii=False))}, "UpdateDateTime" = now() where "Id" = {lit(form["id"])};')
# Workflow havolalari (WorkflowLink) o'chirilmaydi: birinchi qadam "kiruvchi havolasi yo'q qadam" sifatida aniqlanadi,
# havola o'chsa parol tiklash/ro'yxatdan o'tish qadamlari ham "birinchi" bo'lib qoladi va login buziladi.
EOF

psql_q <<'EOF'
-- tillar: o'zbek (standart, DefaultLanguage=uz) va ingliz
insert into "Languages" ("Code", "CreateDateTime", "UpdateDateTime") select 'uz', now(), now() where not exists (select 1 from "Languages" where "Code" = 'uz');
delete from "Languages" where "Code" = 'fr';
delete from "Translations" where "Key" in ('language_description_uz') or ("Key" = 'language_description_en' and "Language" = 'uz');
insert into "Translations" ("Key", "Language", "Value") values
  ('language_description_uz', 'uz', 'O''zbekcha'), ('language_description_uz', 'en', 'Uzbek'), ('language_description_en', 'uz', 'Inglizcha');
-- ilova (klient) nomlari: o'zbekcha nusxa bo'lmasa consent sahifasida nom bo'sh chiqadi
insert into "Translations" ("Key", "Language", "Value", "ClientId")
  select t."Key", 'uz', t."Value", t."ClientId" from "Translations" t
  where t."Language" = 'en' and t."ClientId" is not null and t."Key" <> 'logo_uri'
    and not exists (select 1 from "Translations" u where u."ClientId" = t."ClientId" and u."Key" = t."Key" and u."Language" = 'uz');
-- demo logolar tashqi CDN'dan yuklanadi (cdn.logo.com): bank ichki tarmog'ida ishlamaydi va ortiqcha tashqi so'rov
delete from "Translations" where "Key" = 'logo_uri' and "Value" like 'http%://cdn.logo.com/%';
update "Translations" set "Value" = case "Language" when 'uz' then 'IdM boshqaruv paneli' else 'IdM administration console' end
  where "Key" = 'client_name' and "ClientId" = (select "Id" from "Clients" where "ClientId" = 'SIDS-manager');
-- o'zini o'zi ro'yxatdan o'tkazish va demo tashqi provayderlar (Facebook/Google)
delete from "RegistrationWorkflows";
delete from "AuthenticationSchemeProviders" where "Name" in ('Facebook', 'Google');
EOF

after=$(psql_q -c "select string_agg(\"Code\", ',' order by \"Code\") from \"Languages\"")
echo "login sahifasi sozlandi (tillar: $after)"
[ "$before" = "$after" ] || exit 10
