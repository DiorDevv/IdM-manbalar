#!/usr/bin/env bash
# SimpleIdServer'dagi "LDAP" provisioning'ni tanlangan manba (sinov AD yoki sinov OpenLDAP) uchun sozlaydi va
# to'liq sinxronlashni (extract + import) bajaradi. Admin API orqali ishlaydi, kod o'zgartirilmaydi.
# Qayta-qayta ishga tushirish mumkin: mavjud mapping qoidalari ikkinchi marta qo'shilmaydi.
set -euo pipefail
IDS=https://localhost:5001/master
B=$IDS/provisioning/LDAP

T=$(curl -sk -X POST $IDS/token \
  -d "grant_type=client_credentials&client_id=SIDS-manager&client_secret=password&scope=provisioning" \
  | python3 -c 'import sys,json;print(json.load(sys.stdin)["access_token"])')
H=(-H "Authorization: Bearer $T" -H "Content-Type: application/json")
get() { curl -sk "$B" "${H[@]}"; }

# 1. Ulanish sozlamalari: start-all.sh eksport qilgan LDAP__LDAPRepresentationsExtractionJobOptions__* qiymatlari
#    (manba profili: ad yoki openldap). Delta: versiya = whenChanged/modifyTimestamp + guruhlar xeshi.
V=$(get | python3 -c '
import sys,json
v=dict(json.load(sys.stdin)["values"])
import os
prefix="LDAP__LDAPRepresentationsExtractionJobOptions__"
v.update({k[len(prefix):]:val for k,val in os.environ.items() if k.startswith(prefix)})
print(json.dumps({"values":v}))')
curl -sk -o /dev/null -X PUT "$B/values" "${H[@]}" -d "$V"
echo "sozlamalar yangilandi"

# 2. Mapping qoidalari: login = uid, ism/familiya/email, tabel raqami, bo'lim, lavozim.
MAPPERS=$(get | python3 -c 'import sys,json;print(json.dumps(json.load(sys.stdin)["definition"]["mapping_rules"]))')
SUBJECT_ID=$(echo "$MAPPERS" | python3 -c 'import sys,json;print([m["Id"] for m in json.load(sys.stdin) if m["mapper_type"]==2 and m["usage"]==0][0])')
LOGIN_ATTR=${LDAP_LOGIN_ATTRIBUTE:-uid}; DEPT_ATTR=${LDAP_DEPARTMENT_ATTRIBUTE:-departmentNumber}
curl -sk -o /dev/null -X PUT "$B/mappers/$SUBJECT_ID" "${H[@]}" -d "{\"mapper_type\":2,\"from\":\"$LOGIN_ATTR\",\"usage\":0}"
add_mapper() {
  local from=$1 body=$2
  if echo "$MAPPERS" | python3 -c "import sys,json;sys.exit(0 if any(m['from']=='$from' for m in json.load(sys.stdin)) else 1)"; then
    echo "mapping bor: $from"
  else
    curl -sk -o /dev/null -X POST "$B/mappers" "${H[@]}" -d "$body"
    echo "mapping qo'shildi: $from"
  fi
}
add_mapper givenName      '{"mapper_type":1,"from":"givenName","target_user_property":"Firstname","usage":0}'
add_mapper sn             '{"mapper_type":1,"from":"sn","target_user_property":"Lastname","usage":0}'
add_mapper mail           '{"mapper_type":1,"from":"mail","target_user_property":"Email","usage":0}'
add_mapper employeeNumber '{"mapper_type":0,"from":"employeeNumber","target_user_attribute":"tabel_raqami","usage":0}'
add_mapper "$DEPT_ATTR" "{\"mapper_type\":0,\"from\":\"$DEPT_ATTR\",\"target_user_attribute\":\"bolim\",\"usage\":0}"
add_mapper title          '{"mapper_type":0,"from":"title","target_user_attribute":"lavozim","usage":0}'

# 3. Extract (LDAP'dan o'qish) va Import (IdServer'ga yozish).
status() {
  get | python3 -c "
import sys,json
p=[x for x in json.load(sys.stdin)['processes'] if x['id']=='$1'][0]
e=[x['exceptions'][0][:200] for x in (p.get('errors') or [])]
print(json.dumps({k:p[k] for k in ['is_exported','is_imported','nb_extracted_users','nb_extracted_groups','nb_filtered_representations','nb_imported_users','nb_imported_groups']}), ('XATO: '+str(e)) if e else '')"
}
PID=$(curl -sk "$B/extract" "${H[@]}" | python3 -c 'import sys,json;print(json.load(sys.stdin)["id"])')
echo "extract boshlandi: $PID"
for _ in $(seq 1 60); do sleep 2; S=$(status "$PID"); [[ $S == *'"is_exported": true'* || $S == *XATO* ]] && break; done
echo "extract: $S"
[[ $S == *XATO* ]] && exit 1
curl -sk -o /dev/null "$B/$PID/import" "${H[@]}"
for _ in $(seq 1 60); do sleep 2; S=$(status "$PID"); [[ $S == *'"is_imported": true'* || $S == *XATO* ]] && break; done
echo "import:  $S"
[[ $S == *XATO* ]] && exit 1 || true
