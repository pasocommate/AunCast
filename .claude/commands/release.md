# AunCast リリースコマンド

パッケージの新バージョンをリリースする。
push はこのコマンドでは行わない。コミットとタグまでを実施し、ユーザーが精査してから手動で push する。

## 引数

$ARGUMENTS にバージョン番号が渡される（例: `2.2.0`）。
省略された場合は、現在のバージョンと前タグからの変更内容を踏まえて適切なバージョンを提案し、ユーザーの承認を得る。

## 手順

### 1. 事前チェック

- `git status` で未コミットの変更がないか確認する。未コミットの変更がある場合はユーザーに報告して中断する。
- 現在の `Packages/tokyo.chigiri.pasocommate.auncast/package.json` の `version` を読み取る。
- `git tag --sort=-v:refname` で直前のタグを特定する。

### 2. 更新点の収集

- 直前タグから HEAD までの `git log --format="%s"` を取得する。
- コミットメッセージを分析し、**ユーザーにとって意味のある変更のみ**を抽出する:
  - 新機能（feat）
  - バグ修正（fix）
  - パフォーマンス改善（perf）
  - 破壊的変更
- 以下は除外する:
  - chore, docs, refactor, style, ci, test など内部的な変更
  - マージコミット
- 抽出した変更点を箇条書きで整理し、ユーザーに提示して確認を得る。必要に応じて文言の修正や項目の追加・削除を受け付ける。

### 3. バージョン決定

$ARGUMENTS が指定されている場合はそれを使う。未指定の場合:
- 破壊的変更があればメジャーバージョンを上げる提案をする
- 新機能があればマイナーバージョンを上げる提案をする
- バグ修正のみならパッチバージョンを上げる提案をする
- ユーザーの承認を得る

### 4. CHANGELOG.md の更新

パッケージディレクトリ `Packages/tokyo.chigiri.pasocommate.auncast/CHANGELOG.md` を更新する。

ファイルが存在しない場合は新規作成する。形式は [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) に準拠:

```markdown
# Changelog

## [{バージョン}] - {YYYY-MM-DD}
- 変更点1
- 変更点2
```

ファイルが既に存在する場合は、先頭のヘッダ（`# Changelog` 行）の直後に新バージョンのセクションを挿入する。既存のバージョン履歴はそのまま保持する。

変更点の内容はステップ 2 で確定したものをそのまま使う。

### 5. バージョン番号の更新

以下の３箇所を新バージョンに更新する:

- `Packages/tokyo.chigiri.pasocommate.auncast/package.json` の `version` フィールド
- `Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/Core/AunCastDualPlayerController.cs` の `PACKAGE_VERSION` 定数
- `Manual/src/assets/stylesheets/extra.css` の `header h1::after` の `content` 値

### 6. コミット

以下の形式でコミットする:

```
chore(release): v{バージョン}

v{前バージョン} からの主な変更点:
- 変更点1
- 変更点2
- ...
```

コミット対象は以下のファイル:
- `Packages/tokyo.chigiri.pasocommate.auncast/package.json`
- `Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/Core/AunCastDualPlayerController.cs`
- `Manual/src/assets/stylesheets/extra.css`
- `Packages/tokyo.chigiri.pasocommate.auncast/CHANGELOG.md`

CHANGELOG.md が新規作成の場合は `.meta` ファイルも含める。

### 7. タグ

`git tag {バージョン}` でタグを打つ（`v` プレフィックスなし。既存タグ `2.1.0`, `2.0.0` 等に合わせる）。

### 8. 完了メッセージ

以下をユーザーに通知する:
- 作成したコミットとタグの内容
- push は行っていないこと
- 精査後に `git push && git push --tags` で反映できること
- **⚠️ 要手動対応：VPMPackageAutoInstallerCreator で v{バージョン} 用のインストーラーパッケージを生成し、BOOTH の該当商品の「ファイルの追加・管理」からアップロードすること。**
