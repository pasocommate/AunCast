# UIPanel シェーダー: _MainTex サンプリングの汎用化

## 背景

UIPanel の `_VIDEO_GAMMA` バリアントは RawImage 経由のビデオ RenderTexture 向けに設計されているが、同じ RawImage に sRGB タグ付きの通常 Texture2D（idle 画像など）が差し替えられるケースで以下の不具合が出る。

1. **画面が暗い** — sRGB テクスチャは GPU サンプラが自動でリニア変換済み。`pow(rgb, 2.2)` が重ねて適用され二重補正になる
2. **上下反転** — D3D の RenderTexture は Y 反転するが、UIPanel にはその補正がない

## 方針

- 用途特化のフラグ（`_IsVideoTexture` 等）を追加するのではなく、**ガンマ指数と Y 反転を汎用的なプロパティとして公開**する。コンシューマがテクスチャ種別に応じて値を設定する
- キーワード `_VIDEO_GAMMA` は実態に合わせ `_USE_MAINTEX` にリネームする。役割は「`_MainTex` をテクスチャソースとして使う」の一点

## 変更対象

`Packages/tokyo.chigiri.pasocommate.rendermate/Shaders/UIPanel.shader`

## 変更内容

### 1. プロパティ

旧:

```hlsl
// ---- Video Gamma: RawImage の _MainTex をガンマ補正付きでサンプリングする ----
[Toggle(_VIDEO_GAMMA)] _VideoGamma ("Video Gamma Correction", Float) = 0
```

新:

```hlsl
// ---- MainTex: RawImage の _MainTex をサンプリングする ----
[Toggle(_USE_MAINTEX)] _UseMainTex ("Use MainTex", Float) = 0
_Gamma ("Gamma", Float) = 2.2
_FlipY ("Flip Y", Float) = 0
```

| プロパティ | 型 | デフォルト | 意味 |
|-----------|-----|-----------|------|
| `_UseMainTex` | Toggle | 0 | `_MainTex` をサンプリングソースとして使うかの切替 |
| `_Gamma` | Float | 2.2 | ガンマ補正指数。`1.0` = 補正なし、`2.2` = 標準 sRGB→リニア変換 |
| `_FlipY` | Float | 0 | UV の Y 軸反転。`0` = そのまま、`1` = 反転 |

- 3 プロパティとも Inspector で直接調整可能
- シェーダーバリアント追加なし（`_Gamma`・`_FlipY` はランタイム float）

### 2. キーワード宣言

旧:

```hlsl
#pragma shader_feature_local __ _VIDEO_GAMMA
```

新:

```hlsl
#pragma shader_feature_local __ _USE_MAINTEX
```

### 3. sampler2D _MainTex の常時宣言

`_MainTex` のサンプラ宣言を `_USE_MAINTEX` ガード外に移動する。`_Gamma`・`_FlipY` の変数宣言も同じスコープに置く。

旧:

```hlsl
#if defined(_VIDEO_GAMMA)
sampler2D _MainTex;
#endif
```

新:

```hlsl
sampler2D _MainTex;
float _Gamma;
float _FlipY;
```

### 4. フラグメントシェーダー

旧:

```hlsl
#if defined(_VIDEO_GAMMA)
float4 baseSample = tex2D(_MainTex, IN.uv);
#ifndef UNITY_COLORSPACE_GAMMA
baseSample.rgb = pow(baseSample.rgb, 2.2);
#endif
#else
float2 baseUV = IN.uv * _BaseTex_ST.xy + _BaseTex_ST.zw;
float4 baseSample = tex2D(_BaseTex, baseUV);
#endif
```

新:

```hlsl
#if defined(_USE_MAINTEX)
float2 mainUV = IN.uv;
mainUV.y = lerp(mainUV.y, 1.0 - mainUV.y, _FlipY);
float4 baseSample = tex2D(_MainTex, mainUV);
#ifndef UNITY_COLORSPACE_GAMMA
baseSample.rgb = pow(baseSample.rgb, _Gamma);
#endif
#else
float2 baseUV = IN.uv * _BaseTex_ST.xy + _BaseTex_ST.zw;
float4 baseSample = tex2D(_BaseTex, baseUV);
#endif
```

- `_Gamma = 1.0` → `pow(x, 1.0)` = 恒等変換。sRGB テクスチャへの二重補正を回避
- `_FlipY = 0` → UV そのまま。`1` → Y 反転。D3D の RenderTexture に対応

## 変更しないもの

- `_USE_MAINTEX` 無効時のパス（`_BaseTex` を使う通常パス）
- シェーダーバリアント数（`_VIDEO_GAMMA` → `_USE_MAINTEX` のリネームのみ）

## マイグレーション

既存マテリアルのキーワードが `_VIDEO_GAMMA` → `_USE_MAINTEX` に変わるため、`_VIDEO_GAMMA` が有効な全マテリアルでキーワードの付け替えが必要。RenderMate 側のカスタムシェーダー GUI またはマイグレーションスクリプトで対応する。

対象マテリアル（AunCast リポジトリ内）:

- `Packages/tokyo.chigiri.pasocommate.auncast/Themes/Default/VideoPreview.mat`
- `Assets/PasocomMate/AunCast Themes/MZ/Materials/VideoPreview.mat`

## コンシューマ側（AunCast）の対応

AunCast の `AunCastUiScreen.cs` から、テクスチャ切り替え時にマテリアルのプロパティを更新する。

| テクスチャ | `_Gamma` | `_FlipY` |
|-----------|---------|---------|
| ビデオ RenderTexture | `2.2` | `1` |
| idle Texture2D（sRGB） | `1.0` | `0` |

AunCast 側の実装は UIPanel シェーダー改修の完了後に行う。
