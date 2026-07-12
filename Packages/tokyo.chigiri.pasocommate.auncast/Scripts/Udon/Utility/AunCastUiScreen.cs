
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
        private bool _lastVideoFlipY;
        private bool _hasAppliedRenderTexture;
        /// <summary>起動時に RawImage へ割り当てられていた固定テクスチャ（停止時の復元先）。</summary>
        private Texture _initialTexture;
        private Rect _initialUvRect;
#if UNITY_ANDROID
        /// <summary>_Gamma プロパティを持つ共有マテリアル（UIPanel シェーダー想定）。無ければ null。</summary>
        private Material _gammaMaterial;
        private float _initialGamma;
#endif
        /// <summary>アスペクト比計算のために Start 時に測定した親 RectTransform のサイズ。</summary>
        private Vector2 _uiContainerSize;

        /// <summary>RawImage のキャッシュと、アスペクト比計算用の親コンテナサイズを取得する。</summary>
        private void Start()
        {
            rawImage = GetComponent<RawImage>();
            if (rawImage != null)
            {
                _initialTexture = rawImage.texture;
                _initialUvRect = rawImage.uvRect;
#if UNITY_ANDROID
                CacheGammaState();
#endif
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
            if (rawImage != null)
                rawImage.uvRect = _initialUvRect;
#if UNITY_ANDROID
            ApplyGamma(false);
#endif
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
            bool flipY = renderTexture != null && eventBus != null && eventBus.videoFlipY;
            if (_hasAppliedRenderTexture && renderTexture == lastRenderTexture && flipY == _lastVideoFlipY)
                return;

            if (rawImage != null)
            {
                bool isVideo = renderTexture != null;
                Texture display = renderTexture;
                if (display == null)
                    display = idleTexture != null ? idleTexture : _initialTexture;

                rawImage.texture = display;
                ApplyUvRect(isVideo, flipY);
#if UNITY_ANDROID
                ApplyGamma(isVideo);
#endif
                if (display != null && _uiContainerSize.x > 0f)
                    FitRawImageToAspect(display);
                _hasAppliedRenderTexture = true;
            }
            else
            {
                LogWarning("RawImage missing; cannot apply video texture");
            }

            lastRenderTexture = renderTexture;
            _lastVideoFlipY = flipY;
        }

        private void ApplyUvRect(bool isVideo, bool flipY)
        {
            Rect uvRect = _initialUvRect;
            if (isVideo && flipY)
                uvRect = new Rect(_initialUvRect.x, _initialUvRect.y + _initialUvRect.height, _initialUvRect.width, -_initialUvRect.height);
            rawImage.uvRect = uvRect;
        }

#if UNITY_ANDROID
        /// <summary>共有マテリアルの _Gamma 初期値をキャッシュする。プロパティが無いシェーダーでは何もしない。</summary>
        private void CacheGammaState()
        {
            Material mat = rawImage.material;
            if (mat == null || !mat.HasProperty("_Gamma")) return;

            _gammaMaterial = mat;
            _initialGamma = mat.GetFloat("_Gamma");
        }

        /// <summary>Android の AVPro 出力は sRGB 変換済みのため、映像表示中はシェーダーの手動ガンマ補正を無効化する。</summary>
        private void ApplyGamma(bool isVideo)
        {
            if (_gammaMaterial == null) return;
            _gammaMaterial.SetFloat("_Gamma", isVideo ? 1f : _initialGamma);
        }
#endif

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
