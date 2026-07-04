
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// RawImage にビデオテクスチャを適用し、親 RectTransform に合わせてアスペクト比フィットさせる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastUiScreen : UdonSharpBehaviour
    {
        [SerializeField] private AunCastEventBus eventBus;

        [Tooltip("再生停止中に表示する固定画像。未指定なら初期割り当てのテクスチャへ復元する。")]
        /// <summary>AunCastSettings の idleScreenTexture が再配線処理で転写される。</summary>
        public Texture idleTexture;

        /// <summary>同一 GameObject 上の RawImage をキャッシュ。</summary>
        private RawImage rawImage;
        /// <summary>重複適用を防ぐため、前回設定したテクスチャを保持。</summary>
        private Texture lastRenderTexture;
        private bool _hasAppliedRenderTexture;
        /// <summary>起動時に RawImage へ割り当てられていた固定テクスチャ（停止時の復元先）。</summary>
        private Texture _initialTexture;
        /// <summary>アスペクト比計算のために Start 時に測定した親 RectTransform のサイズ。</summary>
        private Vector2 _uiContainerSize;

        /// <summary>RawImage のキャッシュと、アスペクト比計算用の親コンテナサイズを取得する。</summary>
        private void Start()
        {
            rawImage = GetComponent<RawImage>();
            if (rawImage != null)
            {
                _initialTexture = rawImage.texture;
                Transform parent = rawImage.transform.parent;
                if (parent != null)
                {
                    RectTransform parentRt = parent.GetComponent<RectTransform>();
                    if (parentRt != null)
                        _uiContainerSize = parentRt.rect.size;
                }
            }
            UpdateVideoTexture(eventBus != null ? eventBus.videoTexture : null);
        }

        private void OnEnable()
        {
            if (rawImage != null)
                UpdateVideoTexture(eventBus != null ? eventBus.videoTexture : null);
        }

        private void OnDisable()
        {
            _hasAppliedRenderTexture = false;
        }

        public void OnVideoTextureChanged()
        {
            if (eventBus != null)
                UpdateVideoTexture(eventBus.videoTexture);
        }

        /// <summary>テクスチャを RawImage に適用しアスペクト比フィットさせる。変化がなければスキップ。</summary>
        private void UpdateVideoTexture(Texture renderTexture)
        {
            if (_hasAppliedRenderTexture && renderTexture == lastRenderTexture)
                return;

            if (rawImage != null)
            {
                bool isVideo = renderTexture != null;
                Texture display = renderTexture;
                if (display == null)
                    display = idleTexture != null ? idleTexture : _initialTexture;

                rawImage.texture = display;
                SetMaterialVideoParams(isVideo);
                if (display != null && _uiContainerSize.x > 0f)
                    FitRawImageToAspect(display);
                _hasAppliedRenderTexture = true;
            }
            else
            {
                LogWarning("RawImage missing; cannot apply video texture");
            }

            lastRenderTexture = renderTexture;
        }

        private void SetMaterialVideoParams(bool isVideo)
        {
            Material mat = rawImage.material;
            if (mat == null) return;
            if (mat.HasProperty("_Gamma"))
                mat.SetFloat("_Gamma", isVideo ? 2.2f : 1f);
            if (mat.HasProperty("_FlipY"))
                mat.SetFloat("_FlipY", isVideo ? 1f : 0f);
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

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/AunCastUiScreen] {message}", this);
        }
    }
}
