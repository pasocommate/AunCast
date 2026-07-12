
using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// MeshRenderer のマテリアルにビデオテクスチャを適用する。
    /// sharedMaterials を更新するため、同一マテリアルアセットを共有する他のスクリーンにも自動反映される。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastScreen : UdonSharpBehaviour
    {
        [SerializeField] private AunCastEventBus eventBus;

        [Tooltip("このレンダラーのシェーダーがビデオテクスチャに使用するパラメーター名")]
        /// <summary>シェーダーごとにテクスチャプロパティ名が異なるため、設定で切り替え可能にしている。</summary>
        [FormerlySerializedAs("texParam")]
        public string textureProperty = "_MainTex";

        [Tooltip("上下反転時に SetTextureScale/Offset で更新するテクスチャプロパティ名。空なら Texture Property と同じ。")]
        /// <summary>Unity の _ST はテクスチャプロパティに付随するため、通常は textureProperty と同じ値を使う。</summary>
        public string textureStProperty = "";

        [Tooltip("手動ガンマ補正値のプロパティ名。Android では映像表示中のみ 1.0 が設定される。プロパティが無いシェーダーでは何もしない。")]
        /// <summary>Android の AVPro 出力は sRGB 変換済みのため、シェーダー側の pow 補正を映像表示中だけ無効化する。</summary>
        public string gammaProperty = "_Gamma";

        [Tooltip("ビデオテクスチャを設定するレンダラーのインデックス")]
        /// <summary>マルチマテリアルのレンダラーで、どのスロットに適用するかを指定する。</summary>
        public int rendererIndex = 0;

        [Tooltip("再生停止中に表示する固定画像。未指定なら初期割り当てのテクスチャへ復元する。")]
        /// <summary>AunCastSettings の idleScreenTexture が再配線処理で転写される。</summary>
        public Texture idleTexture;

        /// <summary>同一 GameObject 上のレンダラーをキャッシュ。</summary>
        private Renderer targetRenderer;
        /// <summary>重複適用を防ぐため、前回設定したテクスチャを保持。</summary>
        private Texture lastRenderTexture;
        private bool _lastVideoFlipY;
        private bool _hasAppliedRenderTexture;
        private float _lastMaterialWarnAt;
        private Material _restoreMaterial;
        private string _restoreTextureParam;
        private Texture _restoreTexture;
        private bool _restoreHasTexture;
        private string _restoreStParam;
        private Vector2 _restoreScale;
        private Vector2 _restoreOffset;
        private bool _restoreHasSt;
#if UNITY_ANDROID
        private float _restoreGamma;
        private bool _restoreHasGamma;
#endif

        /// <summary>レンダラーをキャッシュする。</summary>
        private void Start()
        {
            targetRenderer = GetComponent<Renderer>();
            CacheRestoreStateIfNeeded(GetTargetMaterial());
            UpdateVideoTexture(eventBus != null ? eventBus.videoTexture : null);
        }

        private void OnEnable()
        {
            if (_restoreMaterial != null)
                UpdateVideoTexture(eventBus != null ? eventBus.videoTexture : null);
        }

        private void OnDisable()
        {
            RestoreMaterialTextures();
            _hasAppliedRenderTexture = false;
        }

        private void OnDestroy()
        {
            RestoreMaterialTextures();
        }

        public void OnVideoTextureChanged()
        {
            if (eventBus != null)
                UpdateVideoTexture(eventBus.videoTexture);
        }

        /// <summary>テクスチャをレンダラーのマテリアルに適用する。変化がなければスキップして負荷を抑える。</summary>
        private void UpdateVideoTexture(Texture renderTexture)
        {
            bool flipY = renderTexture != null && eventBus != null && eventBus.videoFlipY;
            if (_hasAppliedRenderTexture && renderTexture == lastRenderTexture && flipY == _lastVideoFlipY)
                return;

            EnsureRenderer();
            if (targetRenderer)
            {
                Material rendererMat = GetTargetMaterial();
                if (rendererMat != null)
                {
                    CacheRestoreStateIfNeeded(rendererMat);
                    if (renderTexture == null)
                    {
                        RestoreMaterialTextureTransforms();
#if UNITY_ANDROID
                        RestoreMaterialGamma();
#endif
                        if (idleTexture != null)
                            ApplyTextureToMaterial(rendererMat, idleTexture);
                        else
                            RestoreMaterialTextures();
                    }
                    else
                    {
                        ApplyTextureToMaterial(rendererMat, renderTexture);
                        ApplyVideoTextureTransforms(rendererMat, flipY);
#if UNITY_ANDROID
                        ApplyVideoGamma(rendererMat);
#endif
                    }
                    _hasAppliedRenderTexture = true;
                }
            }
            else
            {
                LogWarning("Renderer missing; cannot apply video texture");
            }

            lastRenderTexture = renderTexture;
            _lastVideoFlipY = flipY;
        }

        /// <summary>指定されたテクスチャプロパティにのみ反映する。</summary>
        private void ApplyTextureToMaterial(Material mat, Texture texture)
        {
            SetTextureIfPropertyExists(mat, textureProperty, texture);
        }

        private void EnsureRenderer()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();
        }

        /// <summary>rendererIndex で指定された sharedMaterial を取得する。範囲外ならインデックス 0 にフォールバック。</summary>
        private Material GetTargetMaterial()
        {
            if (targetRenderer == null) return null;

            Material[] mats = targetRenderer.sharedMaterials;
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

        private void CacheRestoreStateIfNeeded(Material mat)
        {
            if (mat == null) return;
            if (_restoreMaterial != null) return;

            _restoreMaterial = mat;
            CacheTextureState(mat, textureProperty);
            CacheTextureTransformState(mat, GetTextureStProperty());
#if UNITY_ANDROID
            CacheGammaState(mat, gammaProperty);
#endif
        }

        private void CacheTextureState(Material mat, string param)
        {
            if (mat == null || string.IsNullOrEmpty(param)) return;
            if (!mat.HasProperty(param)) return;

            _restoreTextureParam = param;
            _restoreTexture = mat.GetTexture(param);
            _restoreHasTexture = true;
        }

        private void CacheTextureTransformState(Material mat, string param)
        {
            if (mat == null || string.IsNullOrEmpty(param)) return;
            if (!mat.HasProperty(param)) return;

            _restoreStParam = param;
            _restoreScale = mat.GetTextureScale(param);
            _restoreOffset = mat.GetTextureOffset(param);
            _restoreHasSt = true;
        }

        private void RestoreMaterialTextures()
        {
            if (_restoreMaterial == null) return;

            if (_restoreHasTexture && _restoreMaterial.HasProperty(_restoreTextureParam))
                _restoreMaterial.SetTexture(_restoreTextureParam, _restoreTexture);
            RestoreMaterialTextureTransforms();
#if UNITY_ANDROID
            RestoreMaterialGamma();
#endif
        }

#if UNITY_ANDROID
        private void CacheGammaState(Material mat, string param)
        {
            if (mat == null || string.IsNullOrEmpty(param)) return;
            if (!mat.HasProperty(param)) return;

            _restoreGamma = mat.GetFloat(param);
            _restoreHasGamma = true;
        }

        /// <summary>Android の AVPro 出力は sRGB 変換済みのため、映像表示中はシェーダーの手動ガンマ補正を無効化する。</summary>
        private void ApplyVideoGamma(Material mat)
        {
            if (mat == null || !_restoreHasGamma) return;
            mat.SetFloat(gammaProperty, 1f);
        }

        private void RestoreMaterialGamma()
        {
            if (_restoreMaterial == null || !_restoreHasGamma) return;
            if (_restoreMaterial.HasProperty(gammaProperty))
                _restoreMaterial.SetFloat(gammaProperty, _restoreGamma);
        }
#endif

        private void RestoreMaterialTextureTransforms()
        {
            if (_restoreMaterial == null) return;

            if (_restoreHasSt && _restoreMaterial.HasProperty(_restoreStParam))
            {
                _restoreMaterial.SetTextureScale(_restoreStParam, _restoreScale);
                _restoreMaterial.SetTextureOffset(_restoreStParam, _restoreOffset);
            }
        }

        private void ApplyVideoTextureTransforms(Material mat, bool flipY)
        {
            ApplySingleVideoTextureTransform(mat, GetTextureStProperty(), flipY);
        }

        private void ApplySingleVideoTextureTransform(Material mat, string param, bool flipY)
        {
            if (mat == null || string.IsNullOrEmpty(param)) return;
            if (!mat.HasProperty(param))
            {
                if (Time.time - _lastMaterialWarnAt > 2.0f)
                {
                    _lastMaterialWarnAt = Time.time;
                    LogWarning($"Material has no texture ST property: {param}");
                }
                return;
            }

            Vector2 scale = _restoreHasSt && param == _restoreStParam ? _restoreScale : mat.GetTextureScale(param);
            Vector2 offset = _restoreHasSt && param == _restoreStParam ? _restoreOffset : mat.GetTextureOffset(param);
            if (flipY)
            {
                offset.y += scale.y;
                scale.y = -scale.y;
            }

            mat.SetTextureScale(param, scale);
            mat.SetTextureOffset(param, offset);
        }

        private string GetTextureStProperty()
        {
            return string.IsNullOrEmpty(textureStProperty) ? textureProperty : textureStProperty;
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

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/AunCastScreen] {message}", this);
        }
    }
}
