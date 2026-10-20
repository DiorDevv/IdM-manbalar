#!/usr/bin/env bash
# start-all.sh ishga tushirgan serverlarni to'xtatadi (har biri o'z jarayon guruhida ishlaydi).
HERE=$(cd "$(dirname "$0")" && pwd)
for f in "$HERE"/.run/*.pid; do
  [ -e "$f" ] || continue
  pid=$(cat "$f"); name=$(basename "$f" .pid)
  kill -- "-$pid" 2>/dev/null && echo "to'xtatildi: $name"
  rm -f "$f"
done
