using System;
using System.Collections.Generic;
using System.IO;
using PasocomMate.AunCast;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace PasocomMate.AunCast.Dev.Editor
{
    /// <summary>AunCast のマニュアル用スクリーンショット状態をシーン上に再現する。</summary>
    public static class AunCastScreenshotSetupMenu
    {
        private const string AUNCAST_PREFAB_GUID = "0d617877712537147a584eb2a1ef735c";
        private const string MENU_ROOT = "Tools/PasocomMate/AunCast Dev/スクリーンショット/";

        private const string PANEL = "PortablePanel";
        private const string CONTENT_AREA = PANEL + "/ContentScaler/PortableContentArea";
        private const string USER_CONTENT = CONTENT_AREA + "/UserContent";
        private const string STAFF_CONTENT = CONTENT_AREA + "/StaffContent";
        private const string SHARED_CONTENT = CONTENT_AREA + "/SharedContent";
        private const string TOP_BAR = CONTENT_AREA + "/TopBarPadded";
        private const string USER_PADDED = USER_CONTENT + "/UserPadded";
        private const string STAFF_PADDED = STAFF_CONTENT + "/StaffPadded";
        private const string SHARED_PADDED = SHARED_CONTENT + "/SharedPadded";

        private const string SAMPLE_URL = "rtspt://example.com/live/abcd0123";
        private const int OUTPUT_WIDTH = 1440;
        private const int OUTPUT_HEIGHT = 1080;
        private const int RENDER_SCALE = 2;
        private const string VIEWER_PNG_NAME = "portable-panel-viewer.png";
        private const string STAFF_PNG_NAME = "portable-panel-staff.png";
        private const string SAMPLE_USER_COUNTS =
            "Playing\n<size=28>34</size><size=16>+10</size>\n\n"
            + "In Instance\n<size=28>35</size>\n\n"
            + "Queued\n<size=28>1</size>";
        // 実表示と同じく、■→□の順に分け、各グループ内を赤→橙→黄→青→白で並べる。
        // ■34個、赤い□1個。Concurrent上限を使う黄10人と待機中の青1人を表す。
        private const string SAMPLE_INDICATORS =
            "<color=#FF4444>■</color><color=#FFCC33>■■■■■■■■■</color>\n"
            + "<color=#FFCC33>■</color><color=#5599FF>■</color>"
            + "<color=#DDDDDD>■■■■■■■■</color>\n"
            + "<color=#DDDDDD>■■■■■■■■■■</color>\n"
            + "<color=#DDDDDD>■■■■</color><color=#FF4444>□</color>";

        private static readonly Color DRIFT_WARNING_COLOR = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color SILENCE_GAUGE_COLOR = new Color(0.85f, 0.35f, 0.35f, 1f);
        private static readonly Color FALLBACK_USER_BACKGROUND = new Color(0.23f, 0.38f, 0.45f, 1f);
        private static readonly Color FALLBACK_STAFF_BACKGROUND = new Color(0.56f, 0.48f, 0.77f, 1f);

        [MenuItem(MENU_ROOT + "観客ビューを準備", false, 0)]
        public static void PrepareViewerView()
        {
            PrepareView(showStaff: false);
        }

        [MenuItem(MENU_ROOT + "スタッフビューを準備", false, 1)]
        public static void PrepareStaffView()
        {
            PrepareView(showStaff: true);
        }

        [MenuItem(MENU_ROOT + "観客ビューを撮影（透過PNG）", false, 20)]
        public static void CaptureViewerPng()
        {
            CaptureView(showStaff: false, VIEWER_PNG_NAME);
        }

        [MenuItem(MENU_ROOT + "スタッフビューを撮影（透過PNG）", false, 21)]
        public static void CaptureStaffPng()
        {
            CaptureView(showStaff: true, STAFF_PNG_NAME);
        }

        [MenuItem(MENU_ROOT + "観客ビューを準備", true)]
        [MenuItem(MENU_ROOT + "スタッフビューを準備", true)]
        [MenuItem(MENU_ROOT + "観客ビューを撮影（透過PNG）", true)]
        [MenuItem(MENU_ROOT + "スタッフビューを撮影（透過PNG）", true)]
        private static bool ValidatePrepareView()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static Transform PrepareView(bool showStaff)
        {
            Transform root = FindSingleAunCastInstance();
            if (root == null) return null;

            string viewName = showStaff ? "スタッフビュー" : "観客ビュー";
            string undoName = $"Prepare AunCast {viewName} Screenshot";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            try
            {
                PrepareCommon(root, showStaff);
                if (showStaff)
                    PrepareStaff(root);
                else
                    PrepareViewer(root);

                Canvas.ForceUpdateCanvases();
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                Selection.activeGameObject = RequireTransform(root, PANEL).gameObject;
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                Undo.CollapseUndoOperations(undoGroup);
                Debug.Log($"[AunCast Dev] {viewName}をスクリーンショット撮影用の状態にしました。", root);
                return root;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "AunCast 撮影準備",
                    $"{viewName}の準備に失敗しました。\n\n{exception.Message}",
                    "OK");
                return null;
            }
        }

        private static void CaptureView(bool showStaff, string fileName)
        {
            Scene scene = SceneManager.GetActiveScene();
            Camera captureCamera = FindCaptureCamera(scene);
            if (captureCamera == null) return;

            Transform root = PrepareView(showStaff);
            if (root == null) return;

            string outputPath = GetManualAssetPath(fileName);
            try
            {
                bool cornersAreTransparent = CaptureTransparentPng(captureCamera, outputPath);
                Debug.Log(
                    $"[AunCast Dev] {captureCamera.name} から透過PNGを出力しました: {outputPath.Replace('\\', '/')}",
                    captureCamera);

                if (!cornersAreTransparent)
                {
                    EditorUtility.DisplayDialog(
                        "AunCast 撮影完了（注意）",
                        "PNGは出力されましたが、画像の四隅が透明ではありません。\n"
                        + "カメラのポストプロセスや、画面全体を覆うオブジェクトがアルファ値を書き換えていないか確認してください。",
                        "OK");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "AunCast 撮影失敗",
                    $"透過PNGの出力に失敗しました。\n\n{exception.Message}",
                    "OK");
            }
        }

        private static Camera FindCaptureCamera(Scene scene)
        {
            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("AunCast 撮影", "有効なシーンがありません。", "OK");
                return null;
            }

            var cameras = new List<Camera>();
            foreach (Camera camera in UnityEngine.Object.FindObjectsOfType<Camera>(true))
            {
                if (camera == null || camera.gameObject.scene != scene) continue;
                if (!camera.gameObject.activeInHierarchy) continue;
                cameras.Add(camera);
            }

            Camera selectedCamera = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<Camera>()
                : null;
            if (selectedCamera != null && cameras.Contains(selectedCamera))
                return selectedCamera;

            Camera mainCamera = null;
            int mainCameraCount = 0;
            foreach (Camera camera in cameras)
            {
                if (!camera.CompareTag("MainCamera")) continue;
                mainCamera = camera;
                mainCameraCount++;
            }
            if (mainCameraCount == 1)
                return mainCamera;

            if (cameras.Count == 1)
                return cameras[0];

            if (cameras.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "AunCast 撮影",
                    "現在のシーンに有効なCameraがありません。",
                    "OK");
                return null;
            }

            string cameraNames = string.Empty;
            foreach (Camera camera in cameras)
                cameraNames += $"\n・{camera.name}";
            EditorUtility.DisplayDialog(
                "AunCast 撮影",
                "撮影に使用するCameraを特定できません。HierarchyでCameraを１つ選択してから再実行してください。"
                + cameraNames,
                "OK");
            return null;
        }

        private static string GetManualAssetPath(string fileName)
        {
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
                throw new InvalidOperationException("Unityプロジェクトのルートパスを取得できません。");
            return Path.GetFullPath(
                Path.Combine(projectRoot.FullName, "Manual", "src", "assets", fileName));
        }

        private static bool CaptureTransparentPng(Camera camera, string outputPath)
        {
            int renderWidth = OUTPUT_WIDTH * RENDER_SCALE;
            int renderHeight = OUTPUT_HEIGHT * RENDER_SCALE;
            var source = new RenderTexture(
                renderWidth,
                renderHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "AunCastScreenshotSource",
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1,
            };
            var output = new RenderTexture(
                OUTPUT_WIDTH,
                OUTPUT_HEIGHT,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "AunCastScreenshotOutput",
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1,
            };
            var texture = new Texture2D(
                OUTPUT_WIDTH,
                OUTPUT_HEIGHT,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: false);

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            CameraClearFlags previousClearFlags = camera.clearFlags;
            Color previousBackgroundColor = camera.backgroundColor;
            bool previousAllowHdr = camera.allowHDR;
            bool previousAllowMsaa = camera.allowMSAA;
            Rect previousRect = camera.rect;

            try
            {
                source.Create();
                output.Create();

                camera.targetTexture = source;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.rect = new Rect(0f, 0f, 1f, 1f);

                RenderTexture.active = source;
                GL.Clear(clearDepth: true, clearColor: true, backgroundColor: Color.clear);
                camera.Render();

                Graphics.Blit(source, output);
                RenderTexture.active = output;
                texture.ReadPixels(new Rect(0, 0, OUTPUT_WIDTH, OUTPUT_HEIGHT), 0, 0, recalculateMipMaps: false);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                string directory = Path.GetDirectoryName(outputPath);
                if (string.IsNullOrEmpty(directory))
                    throw new InvalidOperationException("PNGの出力先フォルダを取得できません。");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());

                return HasTransparentCorners(texture);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.clearFlags = previousClearFlags;
                camera.backgroundColor = previousBackgroundColor;
                camera.allowHDR = previousAllowHdr;
                camera.allowMSAA = previousAllowMsaa;
                camera.rect = previousRect;
                RenderTexture.active = previousActive;

                source.Release();
                output.Release();
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static bool HasTransparentCorners(Texture2D texture)
        {
            const float alphaTolerance = 1f / 255f;
            int right = texture.width - 1;
            int top = texture.height - 1;
            return texture.GetPixel(0, 0).a <= alphaTolerance
                && texture.GetPixel(right, 0).a <= alphaTolerance
                && texture.GetPixel(0, top).a <= alphaTolerance
                && texture.GetPixel(right, top).a <= alphaTolerance;
        }

        private static void PrepareCommon(Transform root, bool showStaff)
        {
            Transform panel = RequireTransform(root, PANEL);
            SetActive(panel.gameObject, true);

            Canvas panelCanvas = RequireComponent<Canvas>(root, PANEL);
            SetEnabled(panelCanvas, true);
            SetCanvasGroup(RequireComponent<CanvasGroup>(root, CONTENT_AREA), true);
            SetCanvasGroup(RequireComponent<CanvasGroup>(root, USER_CONTENT), !showStaff);
            SetCanvasGroup(RequireComponent<CanvasGroup>(root, STAFF_CONTENT), showStaff);

            SetActive(RequireTransform(root, TOP_BAR + "/SwitchViewButton").gameObject, true);
            SetActive(RequireTransform(root, TOP_BAR + "/StaffLockButton").gameObject, showStaff);

            Color backgroundColor = GetBackgroundColor(root, showStaff);
            SetImageColor(RequireComponent<Image>(root, PANEL + "/ContentScaler/Background"), backgroundColor);

            SetSlider(RequireComponent<Slider>(root, USER_PADDED + "/VolumeSlider"), 0f, 1f, 0.7f);
            SetButtonsInteractable(panel, true);
            SetTextAlpha(RequireTransform(root, SHARED_PADDED), 1f);
        }

        private static void PrepareViewer(Transform root)
        {
            SetText(RequireComponent<TMP_Text>(root, USER_PADDED + "/StateText"), "Playing");

            SetActive(RequireTransform(root, USER_PADDED + "/ModeDisplayGroup").gameObject, true);
            SetActive(RequireTransform(root, USER_PADDED + "/ModeEditGroup").gameObject, false);
            SetText(
                RequireComponent<TMP_Text>(root, USER_PADDED + "/ModeDisplayGroup/ModeDisplayText"),
                "Manual");

            Toggle silenceResync = RequireComponent<Toggle>(root, USER_PADDED + "/SilenceResyncToggle");
            SetToggle(silenceResync, true, false);

            Slider driftGauge = RequireComponent<Slider>(root, USER_PADDED + "/HeadroomGauge");
            SetSlider(driftGauge, 0f, 50f, 50f);
            SetImageColor(
                RequireComponent<Image>(root, USER_PADDED + "/HeadroomGauge/Fill Area/Fill"),
                DRIFT_WARNING_COLOR);

            Slider silenceGauge = RequireComponent<Slider>(root, USER_PADDED + "/SilenceGauge");
            SetSlider(silenceGauge, -60f, 0f, -10f);
            SetImageColor(
                RequireComponent<Image>(root, USER_PADDED + "/SilenceGauge/Fill Area/Fill"),
                SILENCE_GAUGE_COLOR);

            SetTextAlpha(RequireTransform(root, USER_PADDED), 1f);
        }

        private static void PrepareStaff(Transform root)
        {
            SetActive(RequireTransform(root, STAFF_PADDED + "/ConnectionDisplayGroup").gameObject, true);
            SetActive(RequireTransform(root, STAFF_PADDED + "/ConnectionEditGroup").gameObject, false);
            SetActive(RequireTransform(root, STAFF_PADDED + "/ConcurrentDisplayGroup").gameObject, true);
            SetActive(RequireTransform(root, STAFF_PADDED + "/ConcurrentEditGroup").gameObject, false);
            SetActive(RequireTransform(root, STAFF_PADDED + "/DriftThresholdDisplayGroup").gameObject, true);
            SetActive(RequireTransform(root, STAFF_PADDED + "/DriftThresholdEditGroup").gameObject, false);
            SetActive(RequireTransform(root, STAFF_PADDED + "/ForceModeDisplayGroup").gameObject, true);
            SetActive(RequireTransform(root, STAFF_PADDED + "/ForceModeEditGroup").gameObject, false);

            SetText(
                RequireComponent<TMP_Text>(root, STAFF_PADDED + "/NowPlayingArea/NowPlayingArea_Inner/NowPlayingText"),
                SAMPLE_URL);
            SetUrlInput(
                RequireComponent<VRCUrlInputField>(
                    root,
                    STAFF_PADDED + "/NextURLInputField/NextURLInputField_Inner"),
                VRCUrl.Empty);
            SetText(
                RequireComponent<TMP_Text>(
                    root,
                    STAFF_PADDED + "/ConnectionDisplayGroup/ConnectionLimitDisplayText"),
                "100");
            SetText(
                RequireComponent<TMP_Text>(
                    root,
                    STAFF_PADDED + "/ConcurrentDisplayGroup/ConcurrentLimitDisplayText"),
                "10");
            SetText(
                RequireComponent<TMP_Text>(
                    root,
                    STAFF_PADDED + "/DriftThresholdDisplayGroup/DriftThresholdDisplayText"),
                "100 ms");
            SetText(
                RequireComponent<TMP_Text>(root, STAFF_PADDED + "/ForceModeDisplayGroup/ForceModeDisplayText"),
                "No Override");
            SetText(RequireComponent<TMP_Text>(root, STAFF_PADDED + "/IndicatorText"), SAMPLE_INDICATORS);
            SetText(RequireComponent<TMP_Text>(root, STAFF_PADDED + "/UserCountText"), SAMPLE_USER_COUNTS);
            SetText(
                RequireComponent<TMP_Text>(root, SHARED_PADDED + "/HelpArea/HelpArea_Inner/HelpText"),
                "コントロールにホバーでヘルプ表示  [Click here to toggle language]");

            SetToggle(RequireComponent<Toggle>(root, STAFF_PADDED + "/TimelineLoggingToggle"), false, true);
            SetText(
                RequireComponent<TMP_Text>(root, TOP_BAR + "/StaffLockButton/StaffLockButton_Inner/Label"),
                "\uE898");
            SetTextAlpha(RequireTransform(root, STAFF_PADDED), 1f);
        }

        private static Transform FindSingleAunCastInstance()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("AunCast 撮影準備", "有効なシーンがありません。", "OK");
                return null;
            }

            var matches = new List<Transform>();
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sceneRoot);
                if (string.IsNullOrEmpty(assetPath)) continue;
                if (AssetDatabase.AssetPathToGUID(assetPath) != AUNCAST_PREFAB_GUID) continue;
                matches.Add(sceneRoot.transform);
            }

            if (matches.Count == 1) return matches[0];

            string message = matches.Count == 0
                ? "現在のシーンに AunCast.prefab のインスタンスがありません。"
                : "現在のシーンに AunCast.prefab のインスタンスが複数あります。１つだけ配置してください。";
            EditorUtility.DisplayDialog("AunCast 撮影準備", message, "OK");
            return null;
        }

        private static Color GetBackgroundColor(Transform root, bool showStaff)
        {
            AunCastThemeApplier applier = root.GetComponent<AunCastThemeApplier>();
            if (applier != null && applier.theme != null)
                return showStaff ? applier.theme.staffBackgroundColor : applier.theme.userBackgroundColor;
            return showStaff ? FALLBACK_STAFF_BACKGROUND : FALLBACK_USER_BACKGROUND;
        }

        private static Transform RequireTransform(Transform root, string path)
        {
            Transform target = root.Find(path);
            if (target == null)
                throw new InvalidOperationException($"必要なUIが見つかりません: {path}");
            return target;
        }

        private static T RequireComponent<T>(Transform root, string path) where T : Component
        {
            Transform target = RequireTransform(root, path);
            T component = target.GetComponent<T>();
            if (component == null)
                throw new InvalidOperationException($"必要なコンポーネント {typeof(T).Name} がありません: {path}");
            return component;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target.activeSelf == active) return;
            Undo.RecordObject(target, "Prepare AunCast Screenshot");
            target.SetActive(active);
            RecordPrefabOverride(target);
        }

        private static void SetEnabled(Behaviour target, bool enabled)
        {
            if (target.enabled == enabled) return;
            Undo.RecordObject(target, "Prepare AunCast Screenshot");
            target.enabled = enabled;
            RecordPrefabOverride(target);
        }

        private static void SetCanvasGroup(CanvasGroup target, bool visible)
        {
            Undo.RecordObject(target, "Prepare AunCast Screenshot");
            target.alpha = visible ? 1f : 0f;
            target.interactable = visible;
            target.blocksRaycasts = visible;
            RecordPrefabOverride(target);
        }

        private static void SetImageColor(Image target, Color color)
        {
            if (target.color == color) return;
            Undo.RecordObject(target, "Prepare AunCast Screenshot");
            target.color = color;
            RecordPrefabOverride(target);
        }

        private static void SetText(TMP_Text target, string text)
        {
            if (target.text == text) return;
            Undo.RecordObject(target, "Prepare AunCast Screenshot");
            target.text = text;
            RecordPrefabOverride(target);
        }

        private static void SetUrlInput(VRCUrlInputField target, VRCUrl url)
        {
            Undo.RecordObject(target, "Prepare AunCast Screenshot");
            target.SetUrl(url);
            RecordPrefabOverride(target);
        }

        private static void SetToggle(Toggle target, bool isOn, bool interactable)
        {
            Undo.RecordObject(target, "Prepare AunCast Screenshot");
            target.SetIsOnWithoutNotify(isOn);
            target.interactable = interactable;
            RecordPrefabOverride(target);
        }

        private static void SetSlider(Slider target, float min, float max, float value)
        {
            Undo.RecordObject(target, "Prepare AunCast Screenshot");
            target.minValue = min;
            target.maxValue = max;
            target.SetValueWithoutNotify(value);
            RecordPrefabOverride(target);
        }

        private static void SetButtonsInteractable(Transform root, bool interactable)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.interactable == interactable) continue;
                Undo.RecordObject(button, "Prepare AunCast Screenshot");
                button.interactable = interactable;
                RecordPrefabOverride(button);
            }
        }

        private static void SetTextAlpha(Transform root, float alpha)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                Color color = text.color;
                if (Mathf.Approximately(color.a, alpha)) continue;
                Undo.RecordObject(text, "Prepare AunCast Screenshot");
                color.a = alpha;
                text.color = color;
                RecordPrefabOverride(text);
            }
        }

        private static void RecordPrefabOverride(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
