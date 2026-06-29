<#
.SYNOPSIS
  RenderMate 開発用リンク切替スクリプト。

.DESCRIPTION
  隣接リポジトリ ..\..\RenderMate 側の同パッケージ実体を、AunCast の
  埋め込みパッケージ位置へジャンクションで割り当てる。元の埋め込み実体は
  プロジェクト直下 _RenderMateEmbedded.bak へ退避する（Packages 内に
  package.json を残すと Unity が同名パッケージの重複として弾くため）。
  退避先・ジャンクションともに .gitignore で除外済みのため git への影響はない。

  Unity を閉じてから実行すること。

.PARAMETER Action
  link   ... 隣接リポジトリへリンク（埋め込み実体は退避）
  unlink ... 元の埋め込み実体へ復元
  status ... 現在の状態を表示

.EXAMPLE
  pwsh -File Tools/rendermate-dev-link.ps1 status
  pwsh -File Tools/rendermate-dev-link.ps1 link
  pwsh -File Tools/rendermate-dev-link.ps1 unlink
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('link', 'unlink', 'status')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'

# --- パス定義（スクリプト位置基準で解決し、絶対パス羅列を避ける） ---
$root    = Resolve-Path (Join-Path $PSScriptRoot '..')          # プロジェクトルート
$pkg     = Join-Path $root 'Packages\tokyo.chigiri.pasocommate.rendermate'
# 退避先はプロジェクト直下（Packages の外）。Packages 内に package.json を
# 残すと Unity が同名パッケージの重複として弾くため。ルート .gitignore で除外済み。
$bak     = Join-Path $root '_RenderMateEmbedded.bak'
$srcPath = Join-Path $PSScriptRoot '..\..\RenderMate\Packages\tokyo.chigiri.pasocommate.rendermate'

# 隣接リポジトリ側の絶対パス（存在しなければ $null）
$src = $null
if (Test-Path -LiteralPath $srcPath) {
    $src = (Resolve-Path -LiteralPath $srcPath).Path
}

# 指定パスがジャンクション（reparse point）かどうか判定
function Test-Junction([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $item = Get-Item -LiteralPath $path -Force
    return [bool]($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
}

switch ($Action) {
    'status' {
        if (Test-Junction $pkg) {
            Write-Host '[status] LINKED   -> 隣接リポジトリへジャンクション中'
        } else {
            Write-Host '[status] EMBEDDED (埋め込み実体)'
        }
        if (Test-Path -LiteralPath $bak) { Write-Host "[status] 退避フォルダあり: $bak" }
        if ($src) { Write-Host "[status] 隣接リポジトリ: $src" }
        else      { Write-Host "[status] 隣接リポジトリ: 見つかりません ($srcPath)" }
    }

    'link' {
        if (-not $src) {
            throw "隣接リポジトリのパッケージが見つかりません: $srcPath"
        }
        if (Test-Junction $pkg) {
            Write-Host '[skip] 既にリンク済みです'
            break
        }
        if (Test-Path -LiteralPath $bak) {
            throw "退避フォルダが既に存在します: $bak（先に unlink で復元してください）"
        }
        if (Test-Path -LiteralPath $pkg) {
            Write-Host "[link] 埋め込み実体を退避: $pkg -> $bak"
            Move-Item -LiteralPath $pkg -Destination $bak
        }
        Write-Host "[link] ジャンクション作成: $pkg -> $src"
        # mklink はネイティブにジャンクションを張れる cmd 組込みコマンド
        & cmd /c mklink /J "$pkg" "$src" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'mklink に失敗しました' }
        Write-Host '[done] 隣接リポジトリへリンクしました。'
    }

    'unlink' {
        if (Test-Junction $pkg) {
            Write-Host "[unlink] ジャンクション削除: $pkg"
            # ジャンクションは中身を消さずにリンクのみ除去
            (Get-Item -LiteralPath $pkg -Force).Delete()
        }
        elseif (Test-Path -LiteralPath $pkg) {
            # ジャンクションでない実体が既にある＝復元済み or 元から埋め込み。
            # ここで退避フォルダも残っていると入れ子移動の危険があるため止める。
            if (Test-Path -LiteralPath $bak) {
                throw "実体 $pkg と退避 $bak が両方存在します。手動で内容を確認してください。"
            }
            Write-Host '[skip] 既に埋め込み実体です（何もしません）'
            break
        }
        # ここに来るのは pkg が存在しない状態（ジャンクション削除直後 or 元から無し）
        if (Test-Path -LiteralPath $bak) {
            Write-Host "[unlink] 埋め込み実体を復元: $bak -> $pkg"
            Move-Item -LiteralPath $bak -Destination $pkg
            Write-Host '[done] 元の状態へ復元しました。'
        } else {
            Write-Host '[WARN] 退避フォルダがありません。VPM Resolver で再取得してください。'
        }
    }
}
