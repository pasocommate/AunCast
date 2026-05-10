
using JetBrains.Annotations;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// ビデオテクスチャを 3D レンダラー（ワールド空間スクリーン）と
    /// オプションの UI RawImage の両方に適用し、アスペクト比フィッティングを行うハンドラー。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class VideoScreenHandler : UdonSharpBehaviour
    {

        [Header("Renderer")]
        [Tooltip("このレンダラーのシェーダーがビデオテクスチャに使用するパラメーター名")]
        /// <summary>シェーダーごとにテクスチャプロパティ名が異なるため、設定で切り替え可能にしている。</summary>
        public string texParam = "_EmissionMap";

        [Tooltip("ビデオテクスチャを設定するレンダラーのインデックス")]
        /// <summary>マルチマテリアルのレンダラーで、どのスロットに適用するかを指定する。</summary>
        public int rendererIndex = 0;

        [Header("UI")]
        [Tooltip("ビデオテクスチャを表示する RawImage（UI 用、省略可）")]
        /// <summary>デスクトップ UI 等にビデオを同時表示するためのオプション出力先。</summary>
        public RawImage uiRawImage;

        [Header("Debug")]
        [Tooltip("VideoScreenHandler の詳細ログを出力する")]
        public bool verboseLogging = true;

        /// <summary>同一 GameObject 上のレンダラーをキャッシュ。</summary>
        private Renderer targetRenderer;
        /// <summary>重複適用を防ぐため、前回設定したテクスチャを保持。</summary>
        private Texture lastRenderTexture;
        private float _lastNullTextureWarnAt;
        private float _lastMaterialWarnAt;
        /// <summary>アスペクト比計算のために Start 時に測定した親 RectTransform のサイズ。</summary>
        private Vector2 _uiContainerSize;

        /// <summary>レンダラーのキャッシュと、UI アスペクト比計算用の親コンテナサイズを取得する。</summary>
        private void Start()
        {
            targetRenderer = GetComponent<Renderer>();
            if (uiRawImage != null)
            {
                Transform parent = uiRawImage.transform.parent;
                if (parent != null)
                {
                    RectTransform parentRt = parent.GetComponent<RectTransform>();
                    if (parentRt != null)
                        _uiContainerSize = parentRt.rect.size;
                }
            }
            LogVerbose($"Initialized (renderer={(targetRenderer != null)}, texParam={texParam}, index={rendererIndex})");
        }

        /// <summary>現在適用中のテクスチャを返す（外部からの参照用）。</summary>
        [PublicAPI]
        public Texture GetVideoTexture()
        {
            return lastRenderTexture;
        }

        /// <summary>テクスチャをレンダラーと UI の両方に適用する。変化がなければスキップして負荷を抑える。</summary>
        public void UpdateVideoTexture(Texture renderTexture)
        {
            if (renderTexture == lastRenderTexture)
                return;

            if (renderTexture == null)
            {
                float now = Time.time;
                if (now - _lastNullTextureWarnAt > 2.0f)
                {
                    _lastNullTextureWarnAt = now;
                    LogWarning("UpdateVideoTexture received null texture");
                }
            }

            EnsureRenderer();
            if (targetRenderer)
            {
                Material rendererMat = GetTargetMaterial();
                if (rendererMat != null)
                {
                    // 設定値を尊重しつつ、主要プロパティにも反映して表示失敗を避ける
                    SetTextureIfPropertyExists(rendererMat, texParam, renderTexture);
                    if (texParam != "_EmissionMap")
                        SetTextureIfPropertyExists(rendererMat, "_EmissionMap", renderTexture);
                    if (texParam != "_MainTex")
                        SetTextureIfPropertyExists(rendererMat, "_MainTex", renderTexture);
                }
            }
            else
            {
                LogWarning("Renderer missing; cannot apply video texture");
            }

            if (uiRawImage != null)
            {
                uiRawImage.texture = renderTexture;
                if (renderTexture != null && _uiContainerSize.x > 0f)
                    FitRawImageToAspect(renderTexture);
            }

            lastRenderTexture = renderTexture;
            LogVerbose($"Texture updated: {(renderTexture != null ? renderTexture.name : "null")}");
        }

        /// <summary>映像のアスペクト比を保ちつつ、コンテナ内に収まるよう RawImage サイズを調整する。</summary>
        private void FitRawImageToAspect(Texture tex)
        {
            float texAspect = (float)tex.width / tex.height;
            float containerAspect = _uiContainerSize.x / _uiContainerSize.y;
            RectTransform rt = uiRawImage.rectTransform;
            if (texAspect > containerAspect)
                rt.sizeDelta = new Vector2(_uiContainerSize.x, _uiContainerSize.x / texAspect);
            else
                rt.sizeDelta = new Vector2(_uiContainerSize.y * texAspect, _uiContainerSize.y);
        }

        private void EnsureRenderer()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();
        }

        /// <summary>rendererIndex で指定されたマテリアルを取得する。範囲外ならインデックス 0 にフォールバック。</summary>
        private Material GetTargetMaterial()
        {
            if (targetRenderer == null) return null;

            Material[] mats = targetRenderer.materials;
            if (mats == null || mats.Length == 0) return null;

            int idx = rendererIndex;
            if (idx < 0 || idx >= mats.Length)
            {
                if (Time.time - _lastMaterialWarnAt > 2.0f)
                {
                    _lastMaterialWarnAt = Time.time;
                    LogWarning($"rendererIndex out of range: {rendererIndex}, fallback to 0");
                }
                idx = 0;
            }

            return mats[idx];
        }

        /// <summary>プロパティの存在を確認してからテクスチャを設定する。存在しないシェーダーへの誤設定を防ぐ。</summary>
        private void SetTextureIfPropertyExists(Material mat, string param, Texture texture)
        {
            if (mat == null || string.IsNullOrEmpty(param)) return;
            if (!mat.HasProperty(param))
            {
                if (Time.time - _lastMaterialWarnAt > 2.0f)
                {
                    _lastMaterialWarnAt = Time.time;
                    LogWarning($"Material has no property: {param}");
                }
                return;
            }
            mat.SetTexture(param, texture);
        }

        private void LogVerbose(string message)
        {
            if (!verboseLogging) return;
            Debug.Log($"[AunCast/VideoScreenHandler] {message}", this);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/VideoScreenHandler] {message}", this);
        }

    }
}
