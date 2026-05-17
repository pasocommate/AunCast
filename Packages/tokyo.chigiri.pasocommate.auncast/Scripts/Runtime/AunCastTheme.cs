using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// テーマ定義アセット。マテリアル・フォント・配色など UI の外観パラメータを保持する。
    /// プロジェクトに *.asset として保存し、AunCastThemeApplier から参照して使う。
    /// </summary>
    [CreateAssetMenu(
        fileName = "AunCastTheme",
        menuName = "AunCast/Theme",
        order = 0)]
    public class AunCastTheme : ScriptableObject
    {
        [Header("Materials")]
        public Material bgWallMaterial;
        public Material bgPortableMaterial;
        public Material buttonRectMaterial;
        public Material buttonRoundMaterial;
        public Material inputMaterial;
        public Material decal1Material;
        public Material decal2Material;
        public Material handleMaterial;
        public Material hudProgressMaterial;
        public Material al4BandHistoryMaterial;
        public Material alAutoCorrelatorMaterial;

        [Header("Fonts")]
        public TMP_FontAsset displayFont;
        public TMP_FontAsset bodyFont;

        [Header("Panel Background")]
        public Color userBackgroundColor = new Color(0.23f, 0.38f, 0.45f);
        public Color staffBackgroundColor = new Color(0.56f, 0.48f, 0.77f);
        public Color wallBackgroundColor = new Color(0.23f, 0.38f, 0.45f);

        [Header("Decal")]
        public Color decalColor = Color.white;

        [Header("Buttons")]
        public Color primaryColor = new Color(0f, 0.33f, 1f);
        public Color secondaryColor = new Color(0.38f, 0.38f, 0.42f);
        public Color warningColor = new Color(1f, 0.37f, 0f);
        public Color dangerColor = new Color(1f, 0f, 0f);

        [Header("Input")]
        public Color inputBackgroundColor = new Color(0.156862f, 0.156862f, 0.156862f, 1f);

        [Header("Toggle")]
        public Color toggleBackgroundColor = new Color(0.156862f, 0.156862f, 0.156862f, 1f);
        public Color toggleCheckmarkColor = Color.white;

        [Header("Slider")]
        public Color sliderBackgroundColor = new Color(0.156862f, 0.156862f, 0.156862f, 1f);
        public Color sliderFillColor = new Color(0f, 0.33f, 1f);
        public Color sliderHandleColor = new Color(0.5f, 0.5f, 0.5f);

        [Header("Disabled State")]
        [Tooltip("ボタンが無効のときにラベルへ適用するアルファ値 (0〜1)")]
        [Range(0f, 1f)]
        public float disabledButtonLabelAlpha = 0.5f;

        [Header("Button Transition")]
        public ColorBlock buttonTransitionColors = new ColorBlock
        {
            normalColor = new Color(1f, 1f, 1f, 1f),
            highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f),
            pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f),
            selectedColor = new Color(0.96f, 0.96f, 0.96f, 1f),
            disabledColor = new Color(0.50f, 0.50f, 0.50f, 0.8f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f,
        };

        [Header("Text")]
        public Color headingTextColor = Color.white;
        public Color buttonLabelColor = Color.white;
        public Color bodyTextColor = new Color(0.75f, 0.75f, 0.78f);
        public Color inputTextColor = Color.white;
        public Color placeholderTextColor = new Color(0.60f, 0.60f, 0.60f, 1f);

        [Header("HUD Progress (gesture long-press)")]
        [Tooltip("プログレス HUD の表示モード。false=バー、true=パイ。")]
        public bool hudProgressUsePieMode = true;

        [Tooltip("頭部ローカル座標における HUD の配置オフセット（メートル）。Z は前方距離、Y は高さ、X は左右ずらし。")]
        public Vector3 hudProgressLocalOffset = new Vector3(0f, -0.18f, 0.6f);

        [Tooltip("バーのサイズ（メートル）。X=幅、Y=高さ。Pie モードでは無視される。")]
        public Vector2 hudProgressBarSize = new Vector2(0.18f, 0.018f);

        [Tooltip("パイの直径（メートル）。Bar モードでは無視される。")]
        public float hudProgressPieDiameter = 0.09f;

        [Tooltip("リング太さ（0..1）。0 で塗りつぶし円、1 で極細リング。Pie モード専用。")]
        [Range(0f, 1f)] public float hudProgressPieRingThickness = 0.1f;

        [Tooltip("Pie 中央に表示する MSDF デカールテクスチャ。null で無効。")]
        public Texture2D hudProgressDecalTexture;

        [Tooltip("デカール色（HDR・アルファ可）")]
        [ColorUsage(true, true)] public Color hudProgressDecalColor = Color.white;

        [Tooltip("デカールの表示スケール（Pie 直径に対する比率）。X=幅、Y=高さ。")]
        public Vector2 hudProgressDecalScale = new Vector2(0.8f, 0.1f);

        [Tooltip("塗り色（HDR・アルファ可）")]
        [ColorUsage(true, true)] public Color hudProgressFillColor = new Color(0f, 0.75f, 1f, 0.5f);

        [Tooltip("ベース色（HDR・アルファ可）。進捗未到達部分の背景。")]
        [ColorUsage(true, true)] public Color hudProgressBaseColor = new Color(0f, 0f, 0f, 0.5f);
    }
}
