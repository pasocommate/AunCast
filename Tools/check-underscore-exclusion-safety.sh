#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
用途:
  "_" 始まりファイル/フォルダを複製除外する前提で、
  公開対象に参照切れリスクがないか静的チェックする。

チェック内容:
  1) "_" 始まりで除外される .meta の GUID を収集
  2) 除外されないテキストアセットに、その GUID 参照が残っていないか検査
  3) 除外されないテキストアセットに、Assets/.../_... の直書きパスがないか検査

使い方:
  Tools/check-underscore-exclusion-safety.sh
  Tools/check-underscore-exclusion-safety.sh --source-subdir Packages/tokyo.chigiri.pasocommate.auncast
  Tools/check-underscore-exclusion-safety.sh --source-subdir Packages/tokyo.chigiri.pasocommate.rendermate
  Tools/check-underscore-exclusion-safety.sh --source-subdir Packages/tokyo.chigiri.pasocommate.auncast --source-subdir Packages/tokyo.chigiri.pasocommate.rendermate
USAGE
}

is_excluded_relpath() {
  local rel="$1"
  [[ "$rel" == _* || "$rel" == */_* || "$rel" == CLAUDE.md || "$rel" == */CLAUDE.md || "$rel" == CLAUDE.md.meta || "$rel" == */CLAUDE.md.meta ]]
}

is_scan_target_file() {
  local path="$1"
  case "$path" in
    *.unity|*.prefab|*.asset|*.mat|*.controller|*.overrideController|*.playable|*.anim|*.meta|*.asmdef|*.asmref|*.cs|*.shader|*.cginc|*.compute|*.json|*.yaml|*.yml|*.txt|*.md|*.uxml|*.uss|*.xml)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

check_subdir() {
  local subdir="$1"
  if [ ! -d "$subdir" ]; then
    echo "[ERROR] ディレクトリが存在しません: $subdir" >&2
    return 1
  fi

  local -a excluded_entries=()
  local -a excluded_meta_candidates=()
  local -a scan_files=()
  local p rel

  while IFS= read -r -d '' p; do
    rel="${p#"$subdir"/}"

    if is_excluded_relpath "$rel"; then
      excluded_entries+=("$p")

      if [ -f "$p" ] && [[ "$p" == *.meta ]]; then
        excluded_meta_candidates+=("$p")
      fi
      if [ -f "$p.meta" ]; then
        excluded_meta_candidates+=("$p.meta")
      fi
    fi

    if [ -f "$p" ] && ! is_excluded_relpath "$rel" && is_scan_target_file "$p"; then
      scan_files+=("$p")
    fi
  done < <(find "$subdir" -mindepth 1 -print0)

  local -A excluded_meta_map=()
  local meta
  for meta in "${excluded_meta_candidates[@]}"; do
    excluded_meta_map["$meta"]=1
  done

  local -A guid_owner=()
  local guid
  for meta in "${!excluded_meta_map[@]}"; do
    guid="$(sed -n 's/^guid:[[:space:]]*//p' "$meta" | head -n 1 || true)"
    if [ -n "${guid}" ]; then
      guid_owner["$guid"]="$meta"
    fi
  done

  local failed=0
  local tmp_guid_hits
  local tmp_path_hits
  tmp_guid_hits="$(mktemp)"
  tmp_path_hits="$(mktemp)"
  trap 'rm -f "$tmp_guid_hits" "$tmp_path_hits"' RETURN

  echo "=== Check: $subdir ==="
  echo "除外候補エントリ数: ${#excluded_entries[@]}"
  echo "除外 .meta GUID 数 : ${#guid_owner[@]}"
  echo "検査対象ファイル数 : ${#scan_files[@]}"

  if [ "${#scan_files[@]}" -gt 0 ] && [ "${#guid_owner[@]}" -gt 0 ]; then
    local hit_tmp
    hit_tmp="$(mktemp)"
    trap 'rm -f "$tmp_guid_hits" "$tmp_path_hits" "$hit_tmp"' RETURN

    for guid in "${!guid_owner[@]}"; do
      if printf '%s\0' "${scan_files[@]}" | xargs -0 rg -n --no-heading --fixed-strings --color never -- "$guid" > "$hit_tmp" 2>/dev/null; then
        while IFS= read -r line; do
          printf '%s | guid=%s | excluded_meta=%s\n' "$line" "$guid" "${guid_owner[$guid]}" >> "$tmp_guid_hits"
        done < "$hit_tmp"
      fi
    done
  fi

  local asset_path_pattern='(Assets|Packages)[\\/][^[:space:]]*[\\/]_[^[:space:]]*'
  if [ "${#scan_files[@]}" -gt 0 ]; then
    if printf '%s\0' "${scan_files[@]}" | xargs -0 rg -n --no-heading --color never -e "$asset_path_pattern" > "$tmp_path_hits" 2>/dev/null; then
      :
    fi
  fi

  if [ -s "$tmp_guid_hits" ]; then
    failed=1
    echo "[NG] 除外予定 GUID への参照が残っています。" >&2
    sed -n '1,200p' "$tmp_guid_hits" >&2
  else
    echo "[OK] 除外予定 GUID 参照は検出されませんでした。"
  fi

  if [ -s "$tmp_path_hits" ]; then
    failed=1
    echo "[NG] Assets/.../_... または Packages/.../_... の直書きパスが残っています。" >&2
    sed -n '1,200p' "$tmp_path_hits" >&2
  else
    echo "[OK] Assets/.../_... / Packages/.../_... の直書きパスは検出されませんでした。"
  fi

  if [ "$failed" -ne 0 ]; then
    echo "結果: FAILED ($subdir)" >&2
    return 1
  fi

  echo "結果: PASSED ($subdir)"
  return 0
}

main() {
  local -a subdirs=()

  while [ "$#" -gt 0 ]; do
    case "$1" in
      --source-subdir)
        if [ "$#" -lt 2 ]; then
          echo "[ERROR] --source-subdir には引数が必要です。" >&2
          usage
          exit 2
        fi
        subdirs+=("$2")
        shift 2
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        echo "[ERROR] 不明な引数: $1" >&2
        usage
        exit 2
        ;;
    esac
  done

  if [ "${#subdirs[@]}" -eq 0 ]; then
    subdirs=("Packages/tokyo.chigiri.pasocommate.auncast" "Packages/tokyo.chigiri.pasocommate.rendermate")
  fi

  local status=0
  local subdir
  for subdir in "${subdirs[@]}"; do
    if ! check_subdir "$subdir"; then
      status=1
    fi
  done

  exit "$status"
}

main "$@"
