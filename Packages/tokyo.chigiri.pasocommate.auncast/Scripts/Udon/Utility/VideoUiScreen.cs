
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// RawImage にビデオテクスチャを適用し、親 RectTransform に合わせてアスペクト比フィットさせる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class VideoUiScreen : UdonSharpBehaviour
    {
        [Header("Debug")]
        [Tooltip("VideoUiScreen の詳細ログを出力する")]
        public bool verboseLogging = true;

        /// <summary>同一 GameObject 上の RawImage をキャッシュ。</summary>
        private RawImage rawImage;
        /// <summary>重複適用を防ぐため、前回設定したテクスチャを保持。</summary>
        private Texture lastRenderTexture;
        private float _lastNullTextureWarnAt;
        /// <summary>アスペクト比計算のために Start 時に測定した親 RectTransform のサイズ。</summary>
        private Vector2 _uiContainerSize;

        /// <summary>RawImage のキャッシュと、アスペクト比計算用の親コンテナサイズを取得する。</summary>
        private void Start()
        {
            rawImage = GetComponent<RawImage>();
            if (rawImage != null)
            {
                Transform parent = rawImage.transform.parent;
                if (parent != null)
                {
                    RectTransform parentRt = parent.GetComponent<RectTransform>();
                    if (parentRt != null)
                        _uiContainerSize = parentRt.rect.size;
                }
            }
            LogVerbose($"Initialized (rawImage={(rawImage != null)}, containerSize={_uiContainerSize})");
        }

        /// <summary>テクスチャを RawImage に適用しアスペクト比フィットさせる。変化がなければスキップ。</summary>
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

            if (rawImage != null)
            {
                rawImage.texture = renderTexture;
                if (renderTexture != null && _uiContainerSize.x > 0f)
                    FitRawImageToAspect(renderTexture);
            }
            else
            {
                LogWarning("RawImage missing; cannot apply video texture");
            }

            lastRenderTexture = renderTexture;
            LogVerbose($"Texture updated: {(renderTexture != null ? renderTexture.name : "null")}");
        }

        /// <summary>映像のアスペクト比を保ちつつ、コンテナ内に収まるよう RawImage サイズを調整する。</summary>
        private void FitRawImageToAspect(Texture tex)
        {
            float texAspect = (float)tex.width / tex.height;
            float containerAspect = _uiContainerSize.x / _uiContainerSize.y;
            RectTransform rt = rawImage.rectTransform;
            if (texAspect > containerAspect)
                rt.sizeDelta = new Vector2(_uiContainerSize.x, _uiContainerSize.x / texAspect);
            else
                rt.sizeDelta = new Vector2(_uiContainerSize.y * texAspect, _uiContainerSize.y);
        }

        private void LogVerbose(string message)
        {
            if (!verboseLogging) return;
            Debug.Log($"[AunCast/VideoUiScreen] {message}", this);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/VideoUiScreen] {message}", this);
        }
    }
}
