#!/usr/bin/env bash
# every version's firmware kept on the site itself: the newest in fw/<board>/, each older one in
# fw/<version>/<board>/, fw/versions.json lists them all, the newest first. a run puts its own build
# in fw/ and takes the older ones from the site as it is now, and the versions from before the site
# kept them from the workflow runs that built them (archive-seed.txt). nothing is built again.
# the list only grows: a version the site has and this run can't fetch stops the run before it
# publishes, and so does a lower number than one the site has
#   archive.sh <fw> <version> <version text> <site url> <seed list>
# <fw> holds a folder a board, as the firmware job leaves them, their manifests made. needs curl,
# and gh (GH_TOKEN, GH_REPO, actions: read) only while a seed is still to be fetched
#
# GitHub releases were the first try: the workflow's token may not make a tag on this repository's
# commits (403 "Resource not accessible by integration", each of them changes .github/workflows),
# not even on an empty commit of its own over one
set -euo pipefail
FW="$1"
VER="$2"
TEXT="$3"
URL="${4%/}"
SEEDS="$5"
FILES="bootloader.bin partition-table.bin owlos-flight-radar.bin flash_args"

newer() {   # 1 if version $1 is above $2
  [ "$1" != "$2" ] && [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | tail -n 1)" = "$1" ]
}

# what the site keeps now, one line a version: version|text|dir|board board ...
# versions.json is this script's own, one version a line, read back the way it is written
live=$(mktemp)
# Pages answers 503 now and then, and a run asks it for hundreds of files: a passing error is asked again
# (curl's --retry: 408, 429, 500, 502, 503, 504 and timeouts). an answer that stays bad still stops the run
RETRY="--retry 4 --retry-delay 5"
code=$(curl -sS $RETRY -o "$live.json" -w '%{http_code}' "$URL/fw/versions.json?run=${GITHUB_RUN_ID:-0}")
case "$code" in
  200) sed -n 's/.*"version": "\([^"]*\)", "text": "\([^"]*\)", "dir": "\([^"]*\)", "boards": \[ \(.*\) \].*/\1|\2|\3|\4/p' \
         "$live.json" > "$live" ;;
  404) echo "the site keeps no versions yet"
       : > "$live" ;;
  *)   echo "the site's fw/versions.json answered $code: stopping, or the versions it keeps would be lost" >&2
       exit 1 ;;
esac

kept=$(mktemp)                                   # version|text of each older one, as they get here
while IFS='|' read -r v text dir boards <&3; do
  if newer "$v" "$VER"; then
    echo "CHANGES.md says $VER, the site has $v: a new version needs a higher number" >&2
    exit 1
  fi
  [ "$v" = "$VER" ] && continue                  # the same number: this build is that version now
  for b in $(echo "$boards" | tr -d '",'); do
    mkdir -p "$FW/$v/$b"
    for f in $FILES; do
      code=$(curl -sS $RETRY -o "$FW/$v/$b/$f" -w '%{http_code}' "$URL/$dir/$b/$f?run=${GITHUB_RUN_ID:-0}")
      if [ "$code" != 200 ]; then
        echo "$URL/$dir/$b/$f answered $code: stopping, or $v would be lost" >&2
        exit 1
      fi
    done
  done
  echo "$v|$text" >> "$kept"
  echo "$v: $(echo "$boards" | tr -d '",'), from the site"
done 3< "$live"

# the versions from before, from their runs, while the site does not have them yet
while read -r v run text <&3; do
  case "$v" in ""|\#*) continue ;; esac
  if [ "$v" = "$VER" ] || grep -q "^$v|" "$kept"; then
    continue
  fi
  tmp=$(mktemp -d)
  gh run download "$run" -p 'fw-*' -D "$tmp"
  while read -r fa; do                           # each board's files, wherever the download put them
    b=$(basename "$(dirname "$fa")")
    mkdir -p "$FW/$v/$b"
    cp "$(dirname "$fa")"/* "$FW/$v/$b/"
  done < <(find "$tmp" -name flash_args)
  echo "$v|$text" >> "$kept"
  echo "$v: $(cd "$FW/$v" && ls | tr '\n' ' '), from run $run"
done 3< "$SEEDS"

# one entry of versions.json: the version, its text, its folder from the page, the boards in it.
# all of them are ours (CHANGES.md, the git log, make-manifests.sh's names): nothing to escape
entry() {   # <version> <text> <dir as the page has it> <dir here>
  local boards="" m
  for m in "$4"/*/manifest.json; do
    if [ -f "$m" ]; then
      boards="$boards${boards:+, }\"$(basename "$(dirname "$m")")\""
    fi
  done
  printf '    { "version": "%s", "text": "%s", "dir": "%s", "boards": [ %s ] }' "$1" "$2" "$3" "$boards"
}

list=$(entry "$VER" "$TEXT" "fw" "$FW")
while IFS='|' read -r v text; do
  bash .github/make-manifests.sh "$FW/$v" "$text" > /dev/null
  list="$list,"$'\n'"$(entry "$v" "$text" "fw/$v" "$FW/$v")"
done < <(sort -t '|' -k 1,1 -V -r "$kept")
printf '{\n  "versions": [\n%s\n  ]\n}\n' "$list" > "$FW/versions.json"
cat "$FW/versions.json"
