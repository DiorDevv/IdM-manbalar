#!/usr/bin/env bash
# Bank IdM sinov AD — haqiqiy Windows Server 2025 domen kontrolleri (QEMU/KVM, root kerak emas).
#   ./sinov-ad-win/vm.sh create      — bir marta: sirlar, LDAPS sertifikati, sozlama diski, Windows'ni avtomatik o'rnatish (~30–60 daq)
#   ./sinov-ad-win/vm.sh start|stop  — yoqish / to'g'ri o'chirish (ACPI)
#   ./sinov-ad-win/vm.sh status      — VM, LDAPS va o'rnatish bosqichi
#   ./sinov-ad-win/vm.sh screenshot F.png — ekran (o'rnatishni kuzatish uchun)
# Portlar (faqat 127.0.0.1): LDAPS 1636 → 636, LDAP 1389 → 389, VNC :15 (5915).
# Sirlar: .secrets/ad.env (start-all.sh shuni o'qiydi), CA: .secrets/ad-ca.pem. VM fayllari: ~/VMs/win2025 (repodan tashqarida).
# ISO: Microsoft Evaluation Center, Windows Server 2025 (180 kunlik baholash litsenziyasi).
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(dirname "$HERE")
VMDIR=${VMDIR:-$HOME/VMs/win2025}
ISO=${ISO:-$VMDIR/WindowsServer2025_Eval_en-us.iso}
DISK=$VMDIR/dc1.qcow2; CFGISO=$VMDIR/config.iso; PID=$VMDIR/qemu.pid; QMP=$VMDIR/qmp.sock
SECRETS=$ROOT/.secrets; ENV=$SECRETS/ad.env
MEM=${VM_MEMORY:-3072}; CPUS=${VM_CPUS:-2}

running() { [ -f "$PID" ] && kill -0 "$(cat "$PID")" 2>/dev/null; }
# O'rnatish rejimida ishga tushganmi (sozlama diski ulangan)
running_install() { running && tr '\0' ' ' < "/proc/$(cat "$PID")/cmdline" | grep -q config.iso; }
qmp() {  # bitta QMP buyrug'i (JSON)
  python3 -I - "$QMP" "$1" <<'PY'
import json, socket, sys
s = socket.socket(socket.AF_UNIX); s.connect(sys.argv[1]); f = s.makefile('rw')
f.readline(); f.write(json.dumps({"execute": "qmp_capabilities"}) + "\n"); f.flush(); f.readline()
f.write(sys.argv[2] + "\n"); f.flush()
while True:
    line = json.loads(f.readline())
    if "return" in line or "error" in line: print(json.dumps(line)); break
PY
}

LDAPSEARCH=$(command -v ldapsearch || echo "$HOME/.local/root/usr/bin/ldapsearch")
export LD_LIBRARY_PATH="${LD_LIBRARY_PATH:+$LD_LIBRARY_PATH:}$HOME/.local/root/usr/lib/x86_64-linux-gnu"

launch() {  # $1 — "install": birinchi marta Windows ISO'dan, sozlama diski (parollar bilan) faqat shunda ulanadi
  local boot=(-boot order=c) media=()
  if [ "${1:-}" = install ]; then
    boot=(-boot order=c,once=d)
    media=(-drive file="$ISO",if=none,id=cd0,media=cdrom,readonly=on -device ide-cd,drive=cd0,bus=ahci.1
           -drive file="$CFGISO",if=none,id=cd1,media=cdrom,readonly=on -device ide-cd,drive=cd1,bus=ahci.2)
  fi
  qemu-system-x86_64 -name dc1 -machine q35,accel=kvm -cpu host -smp "$CPUS" -m "$MEM" -rtc base=localtime \
    -device ahci,id=ahci \
    -drive file="$DISK",if=none,id=d0,format=qcow2 -device ide-hd,drive=d0,bus=ahci.0 \
    "${media[@]}" \
    -nic user,model=e1000e,hostfwd=tcp:127.0.0.1:1636-:636,hostfwd=tcp:127.0.0.1:1389-:389 \
    -vnc 127.0.0.1:15 -qmp unix:"$QMP",server=on,wait=off -daemonize -pidfile "$PID" "${boot[@]}"
}

create() {
  [ -f "$ISO" ] || { echo "ISO yo'q: $ISO"; exit 1; }
  [ -f "$DISK" ] && { echo "VM allaqachon bor: $DISK (qayta yaratish uchun avval o'chiring)"; exit 1; }
  command -v qemu-system-x86_64 >/dev/null && command -v xorriso >/dev/null || { echo "kerak: qemu-system-x86 qemu-utils xorriso"; exit 1; }
  mkdir -p "$SECRETS" "$VMDIR"; chmod 700 "$SECRETS" "$VMDIR"
  umask 077
  pw() { echo "$1-$(openssl rand -hex 12)-9aZ"; }
  cat > "$ENV" <<EOF
AD_KIND=windows
AD_ADMIN_PASSWORD=$(pw Adm)
AD_DSRM_PASSWORD=$(pw Dsrm)
AD_SVC_DN=CN=svc-idm,OU=Xizmat,DC=bank,DC=test
AD_SVC_PASSWORD=$(pw Svc)
AD_WRITE_DN=CN=svc-idm-write,OU=Xizmat,DC=bank,DC=test
AD_WRITE_PASSWORD=$(pw Wrt)
AD_LDAPS_PORT=1636
EOF
  . "$ENV"
  echo "sirlar: $ENV"

  # LDAPS: o'z sinov CA'si; DC sertifikatida stend ulanadigan nomlar ham bor (127.0.0.1, localhost)
  local work; work=$(mktemp -d); trap 'rm -rf "$work"' RETURN
  openssl req -x509 -newkey rsa:3072 -nodes -days 825 -subj "/CN=Bank IdM sinov CA" -keyout "$work/ca.key" -out "$SECRETS/ad-ca.pem" 2>/dev/null
  openssl req -newkey rsa:2048 -nodes -subj "/CN=dc1.bank.test" -keyout "$work/dc1.key" -out "$work/dc1.csr" 2>/dev/null
  printf 'subjectAltName=DNS:dc1.bank.test,DNS:bank.test,DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\nkeyUsage=digitalSignature,keyEncipherment\n' > "$work/ext"
  openssl x509 -req -in "$work/dc1.csr" -CA "$SECRETS/ad-ca.pem" -CAkey "$work/ca.key" -CAcreateserial -days 825 -extfile "$work/ext" -out "$work/dc1.crt" 2>/dev/null
  local pfxpass; pfxpass=$(openssl rand -hex 16)
  openssl pkcs12 -export -inkey "$work/dc1.key" -in "$work/dc1.crt" -certfile "$SECRETS/ad-ca.pem" -passout "pass:$pfxpass" -out "$work/cfg-dc1.pfx"

  # Sozlama diski (IDMCFG): javob fayli, skript, sertifikatlar, xodimlar (HR sinov faylidan)
  mkdir -p "$work/cfg"
  sed "s|@ADMIN_PASSWORD@|$AD_ADMIN_PASSWORD|g" "$HERE/autounattend.xml" > "$work/cfg/autounattend.xml"
  cp "$HERE/setup.ps1" "$work/cfg/setup.ps1"
  cp "$SECRETS/ad-ca.pem" "$work/cfg/ca.crt"; cp "$work/cfg-dc1.pfx" "$work/cfg/dc1.pfx"
  PFX="$pfxpass" DSRM="$AD_DSRM_PASSWORD" SVC="$AD_SVC_PASSWORD" WRT="$AD_WRITE_PASSWORD" \
    python3 -I - "$ROOT/sinov-hr/xodimlar.csv" > "$work/cfg/config.json" <<'PY'
import csv, json, os, sys
people = []
with open(sys.argv[1], encoding="utf-8") as f:
    for r in csv.DictReader(f, delimiter=";"):
        if not r["email"]: continue                      # hali ishga kirmagan (akkaunti IdM yaratadi)
        people.append({"Login": r["email"].split("@")[0], "FirstName": r["firstName"], "LastName": r["lastName"],
                       "EmployeeNumber": r["employeeNumber"], "Department": r["department"], "Title": r["jobTitle"],
                       "Branch": r.get("branch") or "Bosh ofis", "Manager": r["managerEmployeeNumber"] or None})
groups = [("kredit-bolimi", ["a.karimov", "d.rahimova"]), ("buxgalteriya", ["s.toshmatov", "n.yusupova"]),
          ("it-adminlar", ["j.aliyev"]), ("abs-operatorlar", ["z.ergasheva", "a.karimov", "n.yusupova"])]
json.dump({"PfxPassword": os.environ["PFX"], "DsrmPassword": os.environ["DSRM"], "SvcPassword": os.environ["SVC"],
           "WritePassword": os.environ["WRT"], "Branches": sorted({p["Branch"] for p in people}),
           "People": people, "Groups": [{"Name": n, "Members": m} for n, m in groups]}, sys.stdout, ensure_ascii=False, indent=1)
PY
  xorriso -as mkisofs -quiet -V IDMCFG -J -r -o "$CFGISO" "$work/cfg"
  qemu-img create -q -f qcow2 "$DISK" 40G
  launch install
  echo "Windows o'rnatilmoqda (VNC 127.0.0.1:5915). Holat: $0 status"
}

status() {
  running && echo "VM: ishlayapti (pid $(cat "$PID"))" || echo "VM: o'chiq"
  [ -f "$ENV" ] || return 0
  . "$ENV"
  if LDAPTLS_CACERT="$SECRETS/ad-ca.pem" "$LDAPSEARCH" -x -H "ldaps://127.0.0.1:$AD_LDAPS_PORT" -D "$AD_SVC_DN" -w "$AD_SVC_PASSWORD" \
       -b "DC=bank,DC=test" -s base dn >/dev/null 2>&1; then
    echo "LDAPS: tayyor (svc-idm)"
    # O'rnatish tugadi: parollar bor sozlama diski endi kerak emas
    [ -f "$CFGISO" ] && ! running_install && rm -f "$CFGISO" && echo "sozlama diski o'chirildi"
  else echo "LDAPS: hali yo'q"; fi
}

case "${1:-}" in
  create) create ;;
  start) running && echo "ishlayapti" || { launch; echo "yoqildi"; } ;;
  stop) running && { qmp '{"execute":"system_powerdown"}' >/dev/null; echo "o'chirilmoqda (ACPI)"; } || echo "o'chiq" ;;
  status) status ;;
  screenshot) running || { echo "o'chiq"; exit 1; }; qmp "{\"execute\":\"screendump\",\"arguments\":{\"filename\":\"$(realpath -m "${2:-$VMDIR/screen.png}")\",\"format\":\"png\"}}" ;;
  *) sed -n 2,9p "$0"; exit 1 ;;
esac
