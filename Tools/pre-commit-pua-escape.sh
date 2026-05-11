#!/bin/bash
# .cs ファイル中の PUA リテラル文字を \uXXXX エスケープに自動変換する。
# BMP Private Use Area (U+E000 – U+F8FF) が対象。
#
# 使い方:
#   1) .git/hooks/pre-commit から呼び出す（コミット時に自動エスケープ）
#        #!/bin/bash
#        exec "$(git rev-parse --show-toplevel)/Tools/pre-commit-pua-escape.sh"
#   2) ワンショットで特定ファイルだけ処理する
#        Tools/pre-commit-pua-escape.sh path/to/file.cs ...

set -euo pipefail

# 引数があれば単一ファイルモード（ワンショット）
if [ $# -ge 1 ]; then
    for file in "$@"; do
        if python3 -c "
import sys, os
path = sys.argv[1]
if not os.path.isfile(path):
    print('File not found: ' + path, file=sys.stderr)
    sys.exit(2)
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()
new_content = []
found = False
for ch in content:
    cp = ord(ch)
    if 0xE000 <= cp <= 0xF8FF:
        new_content.append(chr(92) + 'u{:04x}'.format(cp))
        found = True
    else:
        new_content.append(ch)
if not found:
    sys.exit(1)
with open(path, 'w', encoding='utf-8', newline='') as f:
    f.write(''.join(new_content))
" "$file" 2>/dev/null; then
            echo "変換しました: $file"
        else
            echo "PUA リテラルなし: $file"
        fi
    done
    exit 0
fi

fixed_files=()

while IFS= read -r -d '' file; do
    if python3 -c "
import sys

path = sys.argv[1]
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

new_content = []
found = False
for ch in content:
    cp = ord(ch)
    if 0xE000 <= cp <= 0xF8FF:
        new_content.append('\\\\u{:04x}'.format(cp))
        found = True
    else:
        new_content.append(ch)

if not found:
    sys.exit(1)

with open(path, 'w', encoding='utf-8') as f:
    f.write(''.join(new_content))
" "$file" 2>/dev/null; then
        git add -- "$file"
        fixed_files+=("$file")
    fi
done < <(git diff --cached --name-only --diff-filter=ACM -z -- '*.cs')

if [ ${#fixed_files[@]} -gt 0 ]; then
    echo "[pre-commit] PUA リテラルを \\uXXXX に変換しました:"
    for f in "${fixed_files[@]}"; do
        echo "  $f"
    done
fi

exit 0
