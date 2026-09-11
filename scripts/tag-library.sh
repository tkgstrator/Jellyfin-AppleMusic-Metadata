#!/usr/bin/env bash
#
# tag-library.sh — tag a music library with Apple Music ids from the shell.
#
# Two ways to pin Apple Music ids onto a library, so the plugin resolves it
# without a single search.
#
# --nfo (recommended) writes the ids into the .nfo files Jellyfin already
# keeps beside the music. Nothing moves, so play counts, favourites and
# playlists survive:
#
#   <applemusicalbumid>456</applemusicalbumid>
#   <applemusicstorefrontid>jp</applemusicstorefrontid>
#
# The default renames artist directories, album directories and (unless
# --no-tracks) track files instead:
#
#   ROOT/Artist/Album/1 - some title.m4a
#     -> ROOT/Artist-[amid-123]/Album-[amid-456]/01 Title.m4a
#
# Renaming works even when Jellyfin has never matched the library and when
# .nfo saving is switched off, but Jellyfin treats moved files as new items
# and their play counts are lost.
#
# Matching is by exact name only: the artist directory must equal the name of
# one of the first search results, and the album directory must equal the name
# of one of that artist's albums on Apple Music. Names Apple returns are
# sanitised the way the plugin does (/ \ : * ? " < > | become full-width),
# so a directory created from an Apple name still matches.
#
# Nothing is renamed without --apply: the default is a dry run, which writes
# the plan to a file you can read first; --apply reads that same plan. Every
# rename is logged so --undo can put things back.
#
# Requests are sent one at a time, at least one second apart. Apple limits the
# search endpoint per IP and answers 429 for a long time once tripped. On a
# 429 the script backs off (30 s, 60 s, 120 s), then stops and writes the plan
# so far; run it again later — answered lookups are cached on disk and are not
# asked again.
#
# Requires bash 4+, curl, jq. Run it wherever the library is mounted, e.g.
#   docker run --rm -it -v /volume1/music:/music -v "$PWD":/work alpine sh -c \
#     'apk add -q bash curl jq && bash /work/tag-library.sh /music'
#
set -euo pipefail

usage() {
    cat <<'USAGE'
Usage: tag-library.sh [options] ROOT

  ROOT                 Library root: ROOT/<artist>/<album>/<tracks>

Options:
  --nfo                Write the ids into album.nfo / artist.nfo instead of
                       renaming anything. Requires Jellyfin's "Save artwork
                       and metadata into media folders" to stay on
  --dry-run            Write the plan only, change nothing (the default)
  --apply              Perform the changes in the plan
  --plan FILE          Plan file (default: ./tag-library.plan.tsv)
  --undo FILE          Reverse the renames listed in a moves log, then exit
  --no-tracks          Leave track files alone. By default they are renamed
                       to "01 Title.ext" ("1-01 Title.ext" on multi-disc
                       albums), as the plugin's organizer does
  --storefronts LIST   Comma-separated storefronts in order (default: jp,us)
  --cache DIR          Response cache (default: ./tag-library.cache)
  --interval SECONDS   Minimum gap between requests (default: 1)
  --only NAME          Only process the artist directory called NAME
  --quiet              Only print the progress line and the summary
  -h, --help           This text

The plan is a TSV of  kind <TAB> from <TAB> to  — read it before --apply.
With --nfo the rows are  nfo <TAB> file <TAB> the ids to write.
USAGE
}

APPLY=0
DRY_RUN=0
NFO=0
PLAN=./tag-library.plan.tsv
UNDO=
TRACKS=1
STOREFRONTS=jp,us
CACHE=./tag-library.cache
INTERVAL=1
ONLY=
QUIET=0
ROOT=

while [ $# -gt 0 ]; do
    case "$1" in
        --nfo) NFO=1 ;;
        --dry-run) DRY_RUN=1 ;;
        --apply) APPLY=1 ;;
        --plan) PLAN=$2; shift ;;
        --undo) UNDO=$2; shift ;;
        --tracks) TRACKS=1 ;;
        --no-tracks) TRACKS=0 ;;
        --storefronts) STOREFRONTS=$2; shift ;;
        --cache) CACHE=$2; shift ;;
        --interval) INTERVAL=$2; shift ;;
        --only) ONLY=$2; shift ;;
        --quiet) QUIET=1 ;;
        -h|--help) usage; exit 0 ;;
        -*) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
        *) ROOT=$1 ;;
    esac
    shift
done

# --dry-run beats --apply when both are given: the safer reading wins.
[ "$DRY_RUN" -eq 1 ] && APPLY=0

for dep in curl jq; do
    command -v "$dep" >/dev/null || { echo "missing dependency: $dep" >&2; exit 2; }
done
[ "${BASH_VERSINFO[0]}" -ge 4 ] || { echo "bash 4 or newer is required" >&2; exit 2; }

log() { printf '%s\n' "$*" >&2; }

# Detail lines are suppressed by --quiet; the progress line and the summary
# always show.
detail() { [ "$QUIET" -eq 1 ] || printf '%s\n' "$*" >&2; }

# ------------------------------------------------------------- progress -----

PROGRESS_TOTAL=0
PROGRESS_DONE=0
PROGRESS_START=0
PROGRESS_LIVE=0
PROGRESS_LABEL=
PROGRESS_UNIT=artists
# --nfo edits files in place rather than moving them, so "rename" is wrong.
CHANGE_NOUN=$([ "$NFO" -eq 1 ] && echo "id write" || echo rename)

# A single line, rewritten in place, when stderr is a terminal. Redirected to
# a file it would be thousands of identical lines, so there it is printed once
# per artist as an ordinary line instead.
[ -t 2 ] && PROGRESS_LIVE=1

elapsed_hms() {
    local s=$1
    printf '%d:%02d:%02d' $(( s / 3600 )) $(( s % 3600 / 60 )) $(( s % 60 ))
}

# progress [live-only] — with the argument the line is only redrawn on a
# terminal, so a redirected run gets one line per finished artist instead of
# two (one when it starts, one when it ends).
progress() {
    [ "$PROGRESS_TOTAL" -gt 0 ] || return 0
    [ "${1:-}" = live-only ] && [ "$PROGRESS_LIVE" -eq 0 ] && return 0
    local now elapsed eta pct bar filled width=24
    now=$(date +%s)
    elapsed=$(( now - PROGRESS_START ))
    pct=$(( 100 * PROGRESS_DONE / PROGRESS_TOTAL ))
    if [ "$PROGRESS_DONE" -gt 0 ]; then
        eta=$(( elapsed * (PROGRESS_TOTAL - PROGRESS_DONE) / PROGRESS_DONE ))
        eta=$(elapsed_hms "$eta")
    else
        eta="--:--:--"
    fi
    filled=$(( width * PROGRESS_DONE / PROGRESS_TOTAL ))
    bar=$(printf '%*s' "$filled" '' | tr ' ' '#')$(printf '%*s' $(( width - filled )) '')
    if [ "$PROGRESS_LIVE" -eq 1 ]; then
        printf '\r\033[K[%s] %3d%%  %d/%d %s  %d album(s)  %d %s(s)  %s elapsed  ETA %s  %s' \
            "$bar" "$pct" "$PROGRESS_DONE" "$PROGRESS_TOTAL" "$PROGRESS_UNIT" "$albums_matched" "$PLAN_LINES" "$CHANGE_NOUN" \
            "$(elapsed_hms "$elapsed")" "$eta" "${PROGRESS_LABEL:0:28}" >&2
    else
        printf '[%3d%%] %d/%d %s, %d album(s), %d %s(s), %s elapsed, ETA %s — %s\n' \
            "$pct" "$PROGRESS_DONE" "$PROGRESS_TOTAL" "$PROGRESS_UNIT" "$albums_matched" "$PLAN_LINES" "$CHANGE_NOUN" \
            "$(elapsed_hms "$elapsed")" "$eta" "$PROGRESS_LABEL" >&2
    fi
}

# Clears the live progress line so a detail line does not land on top of it.
progress_clear() {
    [ "$PROGRESS_LIVE" -eq 1 ] && [ "$PROGRESS_TOTAL" -gt 0 ] && printf '\r\033[K' >&2
    return 0
}

# ---------------------------------------------------------------- undo ------

if [ -n "$UNDO" ]; then
    [ -f "$UNDO" ] || { log "no such moves log: $UNDO"; exit 2; }
    # Reverse order: artists were renamed after their albums, so they must be
    # put back first for the album paths to exist again.
    tac "$UNDO" | while IFS=$'\t' read -r a b c; do
        if [ "$a" = nfo ]; then
            # b is the .nfo, c the backup taken before it was written.
            if [ "$c" = ABSENT ]; then
                rm -f -- "$b" && log "undo: removed $b"
            elif [ -f "$c" ]; then
                cat "$c" > "$b" && log "undo: restored $b"
            else
                log "skip undo (no backup): $b"
            fi
            continue
        fi
        if [ -e "$b" ] && [ ! -e "$a" ]; then
            mv -n -- "$b" "$a" && log "undo: $b -> $a"
        else
            log "skip undo (state changed): $b"
        fi
    done
    exit 0
fi

[ -n "$ROOT" ] || { usage >&2; exit 2; }
[ -d "$ROOT" ] || { log "no such directory: $ROOT"; exit 2; }
ROOT=${ROOT%/}
mkdir -p "$CACHE"

# ------------------------------------------------------------- helpers ------

# Mirrors FileNames.Sanitize in the plugin: the characters a file name cannot
# hold become their full-width forms, trailing dots and spaces go.
sanitize() {
    local s=$1
    s=${s//\//／}; s=${s//\\/＼}; s=${s//:/：}; s=${s//\*/＊}; s=${s//\?/？}
    s=${s//\"/”}; s=${s//</＜}; s=${s//>/＞}; s=${s//|/｜}
    s=$(printf '%s' "$s" | tr -d '\000-\037')
    s=$(printf '%s' "$s" | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//; s/\.+$//; s/[[:space:]]+$//')
    [ -n "$s" ] && printf '%s' "$s" || printf '_'
}

urlencode() { jq -rn --arg s "$1" '$s|@uri'; }

language_for() {
    case "$1" in
        jp) echo ja-jp ;;
        us) echo en-us ;;
        *) echo en-us ;;
    esac
}

# The [amid-123] tag, if the name carries one.
tag_of() {
    local n=$1
    if [[ $n =~ \[amid-([0-9]+)\] ]]; then printf '%s' "${BASH_REMATCH[1]}"; fi
}

# ---------------------------------------------------------------- token -----

TOKEN=${AM_TOKEN:-}
TOKEN_FILE="$CACHE/webplay-token"

fetch_token() {
    log "fetching the Apple Music web player token"
    local html bundle js t header
    html=$(curl -fsSL -A 'Mozilla/5.0' https://music.apple.com/)
    bundle=$(printf '%s' "$html" | grep -oE '/assets/index~[A-Za-z0-9]+\.js' | head -1)
    [ -n "$bundle" ] || { log "could not find the web player bundle; Apple changed the page"; exit 3; }
    js=$(curl -fsSL -A 'Mozilla/5.0' "https://music.apple.com$bundle")
    # Several JWTs live in the bundle; the web player one has kid "WebPlayKid".
    while read -r t; do
        header=$(printf '%s==' "$(printf '%s' "$t" | cut -d. -f1 | tr '_-' '/+')" | base64 -d 2>/dev/null || true)
        if printf '%s' "$header" | grep -q '"kid":"WebPlayKid"'; then
            TOKEN=$t
            printf '%s' "$t" > "$TOKEN_FILE"
            return 0
        fi
    done < <(printf '%s' "$js" | grep -oE 'eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+')
    log "no web player token in the bundle"
    exit 3
}

if [ -z "$TOKEN" ] && [ -f "$TOKEN_FILE" ]; then
    TOKEN=$(cat "$TOKEN_FILE")
fi
[ -n "$TOKEN" ] || fetch_token

# ------------------------------------------------------------------ api -----

LAST_REQUEST=0
RATE_LIMITED=0

throttle() {
    local now wait
    now=$(date +%s)
    wait=$(( LAST_REQUEST + INTERVAL - now ))
    [ "$wait" -gt 0 ] && sleep "$wait"
    LAST_REQUEST=$(date +%s)
}

# api PATH -> prints the body. Return codes: 0 ok, 1 not found, 2 rate limited.
# Bodies are cached under CACHE keyed by the path, so a re-run replays them.
api() {
    local path=$1 key file dir status body cooldown attempt
    key=$(printf '%s' "$path" | sha256sum | cut -c1-64)
    dir="$CACHE/${key:0:2}"; file="$dir/$key"
    if [ -f "$file" ]; then
        if [ -s "$file" ]; then cat "$file"; return 0; else return 1; fi
    fi
    [ "$RATE_LIMITED" -eq 1 ] && return 2

    mkdir -p "$dir"
    cooldown=30
    for attempt in 1 2 3; do
        throttle
        body=$(curl -sS -A 'Mozilla/5.0' -w '\n%{http_code}' \
            -H "Authorization: Bearer $TOKEN" -H 'Origin: https://music.apple.com' \
            "https://amp-api.music.apple.com$path") || { log "request failed: $path"; return 2; }
        status=${body##*$'\n'}
        body=${body%$'\n'*}
        case "$status" in
            200) printf '%s' "$body" > "$file"; printf '%s' "$body"; return 0 ;;
            404) : > "$file"; return 1 ;;
            401|403)
                if [ "$attempt" -eq 1 ]; then log "token rejected ($status); fetching a fresh one"; fetch_token; continue; fi
                log "still rejected with $status"; return 2 ;;
            429)
                log "rate limited on $path; pausing ${cooldown}s (attempt $attempt of 3)"
                sleep "$cooldown"; cooldown=$(( cooldown * 2 )) ;;
            *) log "unexpected $status for $path"; return 2 ;;
        esac
    done
    log "Apple keeps refusing; stopping here. Run again later — answered lookups are cached."
    RATE_LIMITED=1
    return 2
}

# ------------------------------------------------------------ catalog -------

# find_artist NAME -> "ID<TAB>STOREFRONT" for an exact match, else nothing.
find_artist() {
    local name=$1 sf body id apple rc
    IFS=, read -ra sfs <<< "$STOREFRONTS"
    for sf in "${sfs[@]}"; do
        rc=0
        body=$(api "/v1/catalog/$sf/search?term=$(urlencode "$name")&types=artists&limit=5&l=$(language_for "$sf")") || rc=$?
        [ "$rc" -eq 2 ] && return 2
        [ "$rc" -eq 0 ] || continue
        # Compare the sanitised Apple name with the directory name.
        while IFS=$'\t' read -r id apple; do
            [ -n "$id" ] || continue
            if [ "$(sanitize "$apple")" = "$name" ]; then printf '%s\t%s' "$id" "$sf"; return 0; fi
        done < <(printf '%s' "$body" | jq -r '.results.artists.data[]? | [.id, .attributes.name] | @tsv')
    done
    return 1
}

# artist_albums ID STOREFRONT -> lines of "ID<TAB>NAME", following pagination.
artist_albums() {
    local id=$1 sf=$2 next body rc
    next="/v1/catalog/$sf/artists/$id/albums?limit=100&l=$(language_for "$sf")"
    while [ -n "$next" ]; do
        rc=0
        body=$(api "$next") || rc=$?
        [ "$rc" -eq 0 ] || return "$rc"
        printf '%s' "$body" | jq -r '.data[]? | [.id, .attributes.name] | @tsv'
        next=$(printf '%s' "$body" | jq -r '.next // empty')
        if [ -n "$next" ] && [[ $next != *"l="* ]]; then next="$next&l=$(language_for "$sf")"; fi
    done
}

# album_tracks ID STOREFRONT -> lines of "DISC<TAB>TRACK<TAB>TITLE".
album_tracks() {
    local id=$1 sf=$2 body next rc
    rc=0
    body=$(api "/v1/catalog/$sf/albums/$id?l=$(language_for "$sf")") || rc=$?
    [ "$rc" -eq 0 ] || return "$rc"
    printf '%s' "$body" | jq -r '.data[0].relationships.tracks.data[]? | [(.attributes.discNumber // 1), .attributes.trackNumber, .attributes.name] | @tsv'
    next=$(printf '%s' "$body" | jq -r '.data[0].relationships.tracks.next // empty')
    while [ -n "$next" ]; do
        if [[ $next != *"l="* ]]; then next="$next&l=$(language_for "$sf")"; fi
        rc=0
        body=$(api "$next") || rc=$?
        [ "$rc" -eq 0 ] || return "$rc"
        printf '%s' "$body" | jq -r '.data[]? | [(.attributes.discNumber // 1), .attributes.trackNumber, .attributes.name] | @tsv'
        next=$(printf '%s' "$body" | jq -r '.next // empty')
    done
}

# ----------------------------------------------------------------- plan -----

PLAN_LINES=0

# nfo_value FILE ELEMENT -> the element's text, if the file has it.
nfo_value() {
    [ -f "$1" ] || return 1
    sed -n "s#.*<$2>\([^<]*\)</$2>.*#\1#p" "$1" | head -1
}

# plan_nfo FILE KIND ID STOREFRONT — records an nfo write unless the file
# already says exactly that. KIND is album or artist.
plan_nfo() {
    local file=$1 kind=$2 id=$3 sf=$4 have_id have_sf
    have_id=$(nfo_value "$file" "applemusic${kind}id" || true)
    have_sf=$(nfo_value "$file" "applemusicstorefrontid" || true)
    if [ "$have_id" = "$id" ] && [ "$have_sf" = "$sf" ]; then
        return 1
    fi
    plan_line nfo "$file" "applemusic${kind}id=$id applemusicstorefrontid=$sf"
    return 0
}

# write_nfo FILE KIND ID STOREFRONT — replaces the two elements, or inserts
# them before the closing tag. Creates a minimal file when none exists:
# Jellyfin's parser only reads the elements that are present, so a file
# carrying just a title and the ids does not blank anything out.
write_nfo() {
    local file=$1 kind=$2 id=$3 sf=$4 root title tmp
    root=$([ "$kind" = album ] && echo album || echo artist)
    if [ ! -f "$file" ]; then
        title=$(basename "$(dirname "$file")")
        title=${title//&/\&amp;}; title=${title//</\&lt;}; title=${title//>/\&gt;}
        printf '<?xml version="1.0" encoding="utf-8" standalone="yes"?>\n<%s>\n  <title>%s</title>\n  <applemusic%sid>%s</applemusic%sid>\n  <applemusicstorefrontid>%s</applemusicstorefrontid>\n</%s>\n' \
            "$root" "$title" "$kind" "$id" "$kind" "$sf" "$root" > "$file"
        return 0
    fi
    tmp="$file.amtag.$$"
    sed -e "/<applemusic${kind}id>/d" -e "/<applemusicstorefrontid>/d" \
        -e "s#</$root>#  <applemusic${kind}id>$id</applemusic${kind}id>\n  <applemusicstorefrontid>$sf</applemusicstorefrontid>\n</$root>#" \
        "$file" > "$tmp"
    # Only replace the original once the rewrite has actually produced the
    # elements; a missing closing tag would otherwise silently drop them.
    if grep -q "<applemusic${kind}id>$id</applemusic${kind}id>" "$tmp"; then
        cat "$tmp" > "$file"
        rm -f "$tmp"
        return 0
    fi
    rm -f "$tmp"
    return 1
}

plan_line() {
    printf '%s\t%s\t%s\n' "$1" "$2" "$3" >> "$PLAN"
    PLAN_LINES=$(( PLAN_LINES + 1 ))
}

plan_tracks() {
    local album_dir=$1 album_id=$2 sf=$3 tracks multi disc track title f base ext fdisc ftrack new rc
    rc=0
    tracks=$(album_tracks "$album_id" "$sf") || rc=$?
    [ "$rc" -eq 0 ] || return "$rc"
    [ -n "$tracks" ] || return 0
    multi=0
    while IFS=$'\t' read -r disc track title; do
        [ "${disc:-1}" -gt 1 ] && multi=1
    done <<< "$tracks"
    for f in "$album_dir"/*; do
        [ -f "$f" ] || continue
        base=${f##*/}; ext=${base##*.}; [ "$ext" != "$base" ] || continue
        case "${ext,,}" in mp3|flac|m4a|aac|ogg|oga|opus|wav|wma|aif|aiff|ape|wv|dsf|dff|mpc|alac) ;; *) continue ;; esac
        # Same parse as the plugin: "01 Title", "2-01 Title", "01 - Title".
        if [[ ${base%.*} =~ ^[[:space:]]*(([0-9]{1,2})[-.])?([0-9]{1,3})([[:space:]._-]+|$)(.*)$ ]]; then
            fdisc=${BASH_REMATCH[2]}; ftrack=${BASH_REMATCH[3]}
        else
            continue
        fi
        ftrack=$((10#$ftrack))
        [ -z "$fdisc" ] || fdisc=$((10#$fdisc))
        new=
        while IFS=$'\t' read -r disc track title; do
            [ "$track" = "$ftrack" ] || continue
            [ -z "$fdisc" ] || [ "$disc" = "$fdisc" ] || continue
            if [ "$multi" -eq 1 ]; then
                new=$(printf '%d-%02d %s.%s' "$disc" "$track" "$(sanitize "$title")" "$ext")
            else
                new=$(printf '%02d %s.%s' "$track" "$(sanitize "$title")" "$ext")
            fi
            break
        done <<< "$tracks"
        if [ -n "$new" ] && [ "$new" != "$base" ]; then
            plan_line file "$f" "$album_dir/$new"
            # Lyrics and other sidecars are matched by file name, so they have
            # to travel with the track: "01 Title.lrc" beside "01 Title.flac".
            for side in "$album_dir/${base%.*}".*; do
                [ -f "$side" ] || continue
                [ "$side" != "$f" ] || continue
                plan_line file "$side" "$album_dir/${new%.*}.${side##*.}"
            done
        fi
    done
    return 0
}

: > "$PLAN"
artists_total=0; artists_matched=0; artists_skipped=0; albums_total=0; albums_matched=0

# Counted up front so the progress line has a denominator and an ETA.
artist_dirs=()
for artist_dir in "$ROOT"/*/; do
    artist_dir=${artist_dir%/}
    artist=${artist_dir##*/}
    if [ -n "$ONLY" ] && [ "$artist" != "$ONLY" ]; then continue; fi
    artist_dirs+=("$artist_dir")
done
PROGRESS_TOTAL=${#artist_dirs[@]}
PROGRESS_START=$(date +%s)
log "$PROGRESS_TOTAL artist director$([ "$PROGRESS_TOTAL" -eq 1 ] && echo y || echo ies) under $ROOT"
progress

for artist_dir in "${artist_dirs[@]}"; do
    artist=${artist_dir##*/}
    artists_total=$((artists_total + 1))
    PROGRESS_LABEL=$artist
    progress live-only

    artist_id=$(tag_of "$artist"); sf=
    if [ -n "$artist_id" ]; then
        # Already tagged: the storefront is not in the name, so try them in order.
        IFS=, read -ra sfs <<< "$STOREFRONTS"
        for s in "${sfs[@]}"; do
            if api "/v1/catalog/$s/artists/$artist_id?l=$(language_for "$s")" >/dev/null; then sf=$s; break; fi
        done
        [ "$RATE_LIMITED" -eq 1 ] && break
        if [ -z "$sf" ]; then
            progress_clear; detail "[skip] tagged artist $artist_id not found in any storefront: $artist"
            artists_skipped=$((artists_skipped + 1))
            PROGRESS_DONE=$((PROGRESS_DONE + 1)); progress; continue
        fi
        new_artist_dir=$artist_dir
    else
        rc=0
        hit=$(find_artist "$artist") || rc=$?
        [ "$RATE_LIMITED" -eq 1 ] && break
        if [ "$rc" -ne 0 ]; then
            progress_clear; detail "[skip] no exact artist match: $artist"
            artists_skipped=$((artists_skipped + 1))
            PROGRESS_DONE=$((PROGRESS_DONE + 1)); progress; continue
        fi
        artist_id=${hit%%$'\t'*}; sf=${hit#*$'\t'}
        new_artist_dir=$([ "$NFO" -eq 1 ] && printf '%s' "$artist_dir" || printf '%s' "$ROOT/$(sanitize "$artist")-[amid-$artist_id]")
        if [ "$NFO" -eq 0 ] && [ -e "$new_artist_dir" ]; then
            progress_clear; detail "[skip] target exists: $new_artist_dir"
            artists_skipped=$((artists_skipped + 1))
            PROGRESS_DONE=$((PROGRESS_DONE + 1)); progress; continue
        fi
    fi
    artists_matched=$((artists_matched + 1))

    # The artist's albums are buffered so the header can carry their counts;
    # a bare stream of per-album lines is what made the old log unreadable.
    artist_lines=()
    artist_albums_seen=0
    artist_albums_hit=0
    artist_changes_before=$PLAN_LINES
    artist_note=

    if [ "$NFO" -eq 1 ]; then
        if plan_nfo "$artist_dir/artist.nfo" artist "$artist_id" "$sf"; then
            artist_note="artist.nfo"
        else
            artist_note="artist.nfo already set"
        fi
    fi

    albums=$(artist_albums "$artist_id" "$sf") || albums=
    [ "$RATE_LIMITED" -eq 1 ] && break
    declare -A album_ids=()
    while IFS=$'\t' read -r id name; do
        [ -n "$id" ] || continue
        album_ids["$(sanitize "$name")"]=$id
    done <<< "$albums"

    for album_dir in "$artist_dir"/*/; do
        [ -d "$album_dir" ] || continue
        album_dir=${album_dir%/}
        album=${album_dir##*/}
        albums_total=$((albums_total + 1))
        artist_albums_seen=$((artist_albums_seen + 1))
        album_id=$(tag_of "$album"); new_album_dir=$album_dir
        if [ -z "$album_id" ]; then
            album_id=${album_ids["$album"]:-}
            if [ -z "$album_id" ]; then
                artist_lines+=("    [skip] $album  (no exact match on Apple Music)")
                continue
            fi
            [ "$NFO" -eq 1 ] || new_album_dir="$artist_dir/$(sanitize "$album")-[amid-$album_id]"
        fi
        albums_matched=$((albums_matched + 1))
        artist_albums_hit=$((artist_albums_hit + 1))

        if [ "$NFO" -eq 1 ]; then
            if plan_nfo "$album_dir/album.nfo" album "$album_id" "$sf"; then
                artist_lines+=("    $album"$'\n'"      -> applemusicalbumid=$album_id ($sf)")
            else
                artist_lines+=("    $album  (already set)")
            fi
            continue
        fi

        # Tracks first: their paths are relative to the album directory as it
        # is now, and the plan is applied top to bottom.
        if [ "$TRACKS" -eq 1 ]; then
            plan_tracks "$album_dir" "$album_id" "$sf" || true
            [ "$RATE_LIMITED" -eq 1 ] && break 2
        fi
        if [ "$new_album_dir" != "$album_dir" ]; then
            plan_line dir "$album_dir" "$new_album_dir"
            artist_lines+=("    $album"$'\n'"      -> ${new_album_dir##*/}")
        else
            artist_lines+=("    $album  (already in place)")
        fi
    done
    unset album_ids

    # Artist last, so the album paths above are still valid when applying in order.
    if [ "$new_artist_dir" != "$artist_dir" ]; then
        plan_line dir "$artist_dir" "$new_artist_dir"
        artist_note="${new_artist_dir##*/}"
    fi

    progress_clear
    detail "$artist  [amid-$artist_id] ($sf)  —  $artist_albums_seen album(s), $artist_albums_hit matched, $((PLAN_LINES - artist_changes_before)) $CHANGE_NOUN(s)${artist_note:+  [$artist_note]}"
    for line in "${artist_lines[@]}"; do detail "$line"; done
    [ ${#artist_lines[@]} -gt 0 ] && detail ""
    PROGRESS_DONE=$((PROGRESS_DONE + 1))
    progress
done

progress_clear
log ""
log "artists: $artists_matched of $artists_total matched ($artists_skipped skipped); albums: $albums_matched of $albums_total matched"
log "plan: $PLAN ($(wc -l < "$PLAN") $CHANGE_NOUN(s))"
[ "$RATE_LIMITED" -eq 1 ] && log "stopped early because Apple Music refused; run again later to continue from the cache"

# ---------------------------------------------------------------- apply -----

if [ "$APPLY" -eq 1 ]; then
    MOVES="${PLAN%.tsv}.moves.$(date +%Y%m%d-%H%M%S).log"
    BACKUPS="${PLAN%.tsv}.backups"
    mkdir -p "$BACKUPS"
    applied=0; failed=0
    PROGRESS_TOTAL=$(wc -l < "$PLAN"); PROGRESS_DONE=0; PROGRESS_START=$(date +%s)
    PROGRESS_UNIT=changes; PROGRESS_LABEL=
    # File renames come before their album directory, album directories
    # before their artist directory — the plan was written in that order.
    while IFS=$'\t' read -r kind from to; do
        [ -n "$kind" ] || continue
        PROGRESS_DONE=$((PROGRESS_DONE + 1)); PROGRESS_LABEL=${from##*/}; progress live-only

        if [ "$kind" = nfo ]; then
            # Keep the file as it was, so --undo can put it back byte for byte.
            # A file that did not exist is recorded as such, and undo deletes it.
            backup="$BACKUPS/$(printf '%s' "$from" | sha256sum | cut -c1-32).nfo"
            if [ -f "$from" ]; then cp -p -- "$from" "$backup"; else backup=ABSENT; fi
            id=${to#*applemusic}; id=${id%%id=*}
            value=${to#*id=}; value=${value%% *}
            store=${to##*applemusicstorefrontid=}
            if write_nfo "$from" "$id" "$value" "$store"; then
                printf 'nfo\t%s\t%s\n' "$from" "$backup" >> "$MOVES"; applied=$((applied + 1))
            else
                progress_clear; log "[failed]  could not write ids into $from"; failed=$((failed + 1))
            fi
            continue
        fi

        if [ ! -e "$from" ]; then progress_clear; log "[missing] $from"; failed=$((failed + 1)); continue; fi
        if [ -e "$to" ]; then progress_clear; log "[exists]  $to"; failed=$((failed + 1)); continue; fi
        if mv -n -- "$from" "$to"; then
            printf '%s\t%s\n' "$from" "$to" >> "$MOVES"; applied=$((applied + 1))
        else
            progress_clear; log "[failed]  $from"; failed=$((failed + 1))
        fi
    done < "$PLAN"
    progress_clear
    PROGRESS_LIVE=0; progress
    log "applied $applied change(s), $failed skipped; logged to $MOVES (undo with --undo $MOVES)"
    if [ "$NFO" -eq 1 ]; then
        log "now refresh the library's metadata in Jellyfin: the ids in the .nfo files are read back, without searching"
    else
        log "now run a library scan in Jellyfin: the moved directories are picked up by id, without searching"
    fi
else
    log "dry run — nothing changed. Review the plan, then re-run with --apply"
fi
