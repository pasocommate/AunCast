# AunCast 向け指示

このディレクトリ配下のコードに変更を加える際は、以下のドキュメントを必ず参照してリポジトリのパターンに揃えること。

## 参照すべき設計ドキュメント

- [_Docs/Design.md](_Docs/Design.md)
  — システム全体の設計書（要件・状態遷移・同期モデル）。機能追加・修正時の前提。
- [_Docs/Implementation-Patterns.md](_Docs/Implementation-Patterns.md)
  — **同期変数 / スタッフ権限操作 / UI 双方向追従** のプロジェクト固有コーディングルール。
  以下のような変更を行う際は必ず確認する:
  - `[UdonSynced]` フィールドを追加する
  - スタッフ UI から `LocalDualPlayerController` を操作する機能を追加する
  - Slider / Toggle など UI の状態を全クライアント間で同期する

## 作業時の原則

- 新しい `[UdonSynced]` 変数を追加した後、または既存の同期スキーマを変更した後は、
  `Tools > UdonSharp > Refresh All UdonSharp Programs` を実行して `.asset` を更新する。
- スタッフ専用操作は `SetXxxAsStaff` 命名 + `SetOwner` → 同期書換 → `QueueSerialize` のパターンで統一する。
- **UI に関する変更** (レイアウト・フォント・色・配線など) は `Assets/AunCast-Dev/Scripts/Editor/AunCastSetup.cs` を改変して `_PasocomMate-Dev > AunCast > Setup Scene` で再生成する形で対応する。シーンファイル (`.unity`) は再生成で上書きされるため、変更先は常に `AunCastSetup.cs` にする。
- **Image の生成**は `AddUiImage(GameObject)` ヘルパーを経由する。UIPanel 系シェーダーが TEXCOORD1 に頂点位置を要求するため、`PositionAsUV1` コンポーネントが自動で付く経路を使う。
- **UdonSharp コンポーネントのフィールド配線**は `SerializedObject` / `FindProperty` で書く。`[SerializeField] private` フィールドへのアクセスに、リフレクション (`SetField` 等) やアクセス修飾子の変更 (`private` → `public`) は使わない。外部コンポーネント（AudioLink 等のプレハブインスタンス）は `UdonBehaviour.publicVariables.TrySetVariableValue` + `PrefabUtility.RecordPrefabInstancePropertyModifications` を使う。
- 汎用的な学びがあれば `_Docs/Implementation-Patterns.md` へ追記する（VRChat API の罠は `VRChat-Udon-Development-Notes.md` 側）。
