
using UdonSharp;
using UnityEngine;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// MeshRenderer のマテリアルにビデオテクスチャを適用する。
    /// sharedMaterials を更新するため、同一マテリアルアセットを共有する他のスクリーンにも自動反映される。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class VideoMeshScreen : UdonSharpBehaviour
    {
        [SerializeField] private AunCastEventBus eventBus;

        [Tooltip("このレンダラーのシェーダーがビデオテクスチャに使用するパラメーター名")]
        /// <summary>シェーダーごとにテクスチャプロパティ名が異なるため、設定で切り替え可能にしている。</summary>
        public string texParam = "_EmissionMap";

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
        private bool _hasAppliedRenderTexture;
        private float _lastMaterialWarnAt;
        private Material _restoreMaterial;
        private string _restoreParam0;
        private Texture _restoreTex0;
        private bool _restoreHas0;
        private string _restoreParam1;
        private Texture _restoreTex1;
        private bool _restoreHas1;
        private string _restoreParam2;
        private Texture _restoreTex2;
        private bool _restoreHas2;

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
            if (_hasAppliedRenderTexture && renderTexture == lastRenderTexture)
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
                        // 停止中はアイドル画像を表示。未指定なら初期テクスチャへ復元して白飛びを防ぐ
                        if (idleTexture != null)
                            ApplyTextureToMaterial(rendererMat, idleTexture);
                        else
                            RestoreMaterialTextures();
                    }
                    else
                    {
                        ApplyTextureToMaterial(rendererMat, renderTexture);
                    }
                    _hasAppliedRenderTexture = true;
                }
            }
            else
            {
                LogWarning("Renderer missing; cannot apply video texture");
            }

            lastRenderTexture = renderTexture;
        }

        /// <summary>設定値のプロパティを尊重しつつ、主要プロパティにも反映して表示失敗を避ける。</summary>
        private void ApplyTextureToMaterial(Material mat, Texture texture)
        {
            SetTextureIfPropertyExists(mat, texParam, texture);
            if (texParam != "_EmissionMap")
                SetTextureIfPropertyExists(mat, "_EmissionMap", texture);
            if (texParam != "_MainTex")
                SetTextureIfPropertyExists(mat, "_MainTex", texture);
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
            CacheSingleTexture(mat, texParam);
            if (texParam != "_EmissionMap")
                CacheSingleTexture(mat, "_EmissionMap");
            if (texParam != "_MainTex")
                CacheSingleTexture(mat, "_MainTex");
        }

        private void CacheSingleTexture(Material mat, string param)
        {
            if (mat == null || string.IsNullOrEmpty(param)) return;
            if (!mat.HasProperty(param)) return;
            if (param == _restoreParam0 || param == _restoreParam1 || param == _restoreParam2) return;

            if (!_restoreHas0)
            {
                _restoreParam0 = param;
                _restoreTex0 = mat.GetTexture(param);
                _restoreHas0 = true;
                return;
            }

            if (!_restoreHas1)
            {
                _restoreParam1 = param;
                _restoreTex1 = mat.GetTexture(param);
                _restoreHas1 = true;
                return;
            }

            if (!_restoreHas2)
            {
                _restoreParam2 = param;
                _restoreTex2 = mat.GetTexture(param);
                _restoreHas2 = true;
            }
        }

        private void RestoreMaterialTextures()
        {
            if (_restoreMaterial == null) return;

            if (_restoreHas0 && _restoreMaterial.HasProperty(_restoreParam0))
                _restoreMaterial.SetTexture(_restoreParam0, _restoreTex0);
            if (_restoreHas1 && _restoreMaterial.HasProperty(_restoreParam1))
                _restoreMaterial.SetTexture(_restoreParam1, _restoreTex1);
            if (_restoreHas2 && _restoreMaterial.HasProperty(_restoreParam2))
                _restoreMaterial.SetTexture(_restoreParam2, _restoreTex2);
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
            Debug.LogWarning($"[AunCast/VideoMeshScreen] {message}", this);
        }
    }
}
