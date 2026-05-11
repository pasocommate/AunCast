# AunCast プロジェクト指示書

## 応答言語

- 日本語で簡潔に応答すること。
- コード内のコメントも日本語で書くこと。

## 作業姿勢

- 自信の持てない不明点は憶測で決めつけないこと。
- テスト・ビルドはエージェント判断で実行しないこと。実行が必要な場合は、事前にユーザーへ確認を求め、許可を得てから実行すること。
- 明確な計測手段を伴わない推定値だけで確定しないこと。計測できない場合は推定値であることを明確にすること。

## 参照すべき設計ドキュメント

`Packages/tokyo.chigiri.pasocommate.auncast/` 配下のコードに変更を加える際は、以下を必ず参照してリポジトリのパターンに揃えること。

- [Docs/Design.md](Docs/Design.md)
  — システム全体の設計書（要件・状態遷移・同期モデル）。機能追加・修正時の前提。
- [Docs/Implementation-Patterns.md](Docs/Implementation-Patterns.md)
  — **同期変数 / スタッフ権限操作 / UI 双方向追従** のプロジェクト固有コーディングルール。以下のような変更を行う際は必ず確認する:
  - `[UdonSynced]` フィールドを追加する
  - スタッフ UI から `LocalDualPlayerController` を操作する機能を追加する
  - Slider / Toggle など UI の状態を全クライアント間で同期する
- 汎用的な学びがあれば `Docs/Implementation-Patterns.md` へ追記する。VRChat API の罠は `VRChat-Udon-Development-Notes.md` 側に集約する。

## 作業時の原則（プロジェクト固有）

- 新しい `[UdonSynced]` 変数を追加した後、または既存の同期スキーマを変更した後は、`Tools > UdonSharp > Refresh All UdonSharp Programs` を実行して `.asset` を更新する。
- スタッフ専用操作は `SetXxxAsStaff` 命名 + `SetOwner` → 同期書換 → `QueueSerialize` のパターンで統一する。
- **UI Image を生成する際は `PositionAsUV1` を必ず添付する。** UIPanel 系シェーダーが TEXCOORD1 に頂点位置を要求するため、欠落すると描画が崩れる。
- **UdonSharp コンポーネントのフィールド配線**は `SerializedObject` / `FindProperty` で書く。外部コンポーネント（AudioLink 等のプレハブインスタンス）は `UdonBehaviour.publicVariables.TrySetVariableValue` + `PrefabUtility.RecordPrefabInstancePropertyModifications` を使う。

## UdonSharp スクリプトのルール

- 新しい UdonSharp スクリプト (`.cs`) を作成した場合、対応する `UdonSharpProgramAsset` (`.asset`) も必ずペアで作成すること。`.cs` のみだと UdonBehaviour の `programSource` が空になり、Inspector に "Selected U# behaviour program source reference is not valid." と表示されコンポーネントが動作しない。
  - `.asset` ファイルは同じディレクトリに同名で配置する（例: `VideoMeshScreen.cs` → `VideoMeshScreen.asset`）。
  - テンプレート: 既存の `.asset` ファイル（例: `SyncDebugDisplay.asset`）を参考に、以下のフィールドを書き換えて作成する:
    - `m_Name`: スクリプト名
    - `sourceCsScript` の `guid`: 対象 `.cs.meta` ファイル内の GUID
    - `behaviourSyncMode`: スクリプトの `[UdonBehaviourSyncMode]` に対応する値（0=Continuous, 1=Manual, 2=NoVariableSync）
    - `hasInteractEvent`: `Interact()` をオーバーライドしている場合は 1、そうでなければ 0
    - `compiledVersion: 0` にして、Refresh で再コンパイルさせる
  - `m_Script` の `guid` (`c333ccfdd0cbdbc4ca30cef2dd6e6b9b`) は `UdonSharpProgramAsset` 型を指す固定値で、全アセット共通。
  - `.asset` 作成後に `Tools > UdonSharp > Refresh All UdonSharp Programs` を実行してコンパイルする。スクリプトから行う場合は `UdonSharpProgramAsset.CompileAllCsPrograms(true)`。
- セットアップスクリプト内では、`AddUdonSharpComponent<T>()` や `unity_add_component` を呼ぶ前にプログラムアセット (`.asset`) の存在を GUID 経由で確認し、存在しない場合はエラーを表示して処理をスキップすること。
- コンポーネント追加を先に行ってしまった場合は、既存の UdonBehaviour コンパニオンに対して `programSource` を再リンクする必要がある。`UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour)` で UdonBehaviour を取得し、`SerializedObject` 経由で `programSource` をアサインする。

## 命名規則（VRChat API 準拠）

VRChat の公式 API に倣い、以下のルールに従う。

| 対象 | 規則 | 例 |
|------|------|-----|
| public メソッド | `PascalCase` | `OnButtonPress()`, `GetLocalState()` |
| private メソッド | `PascalCase` | `ComputeGateHeights()`, `UpdateHUD()` |
| Unity ライフサイクル | `PascalCase` | `Start()`, `Update()` |
| UdonSharp オーバーライド | `PascalCase` | `OnDeserialization()`, `Interact()` |
| private フィールド | `_camelCase` | `_localState`, `_currentFall` |
| 定数 | `UPPER_SNAKE_CASE` | `NUM_GATES`, `STATE_IDLE` |
| public プロパティ | `PascalCase` | `StateIdle`, `StateActorRunning` |

- メソッド名は `PascalCase` のみで表現する。アクセスレベルの区別はアクセス修飾子で行う。
- `SendCustomEvent` / `SendCustomEventDelayedSeconds` のターゲットメソッドも通常の `PascalCase` で命名する。

## 文字列リテラルのルール

- 機種依存文字（PUA: BMP U+E000–U+F8FF 等）は `\uXXXX` エスケープシーケンスで表記する。生 PUA をコードに混ぜない。
- Material Symbols アイコン文字は [`MaterialIcons.cs`](Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Editor/MaterialIcons.cs) に `@"\uXXXX"` 定数として集約されている。他のコードでは `MaterialIcons.ArrowUpward` のように定数参照する。
- 生 PUA が紛れ込んだ場合は [`Tools/pre-commit-pua-escape.sh`](Tools/pre-commit-pua-escape.sh) で `\uXXXX` 形式に一括変換できる。`Tools/pre-commit-pua-escape.sh path/to/file.cs` でワンショット実行も可能。

## コマンド実行時のルール

- Bash コマンドはワーキングディレクトリからの相対パスで書く。`cd` は別ディレクトリへの移動が必要な場合のみ使う。
- 複数ファイルを操作するときは `cd` で共通ディレクトリに移動してから相対パスで指定する。絶対パスの羅列は目視監査がしにくいため避ける。

## API の使い方

- **セットアップやツールの都合でアクセス修飾子を変更しない。** `[SerializeField] private` を `public` に変える等、本来の設計を崩す対応は禁止。
- エディタコードから `[SerializeField] private` フィールドに値を設定するには `SerializedObject` / `SerializedProperty` を使う（Unity エディタの正規 API）。
- リフレクション（`GetField` / `SetValue` 等）は上記で対応できるため使わない。
- **`AssetDatabase.LoadAssetAtPath` にパス文字列リテラルを渡さない。** アセットは必ず GUID で特定する。`.meta` ファイルの `guid:` から取得した GUID を `AssetDatabase.GUIDToAssetPath()` でパスに変換してからロードする。`LoadAssetByGuid<T>()` ヘルパーがあればそれを使う。
