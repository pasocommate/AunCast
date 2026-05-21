using System;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// WallControlPanel.prefab に右手ダブルトリガートグルを追加し、
    /// AunCast.prefab の summonGesture を左右両手有効 (12) に更新する使い捨てスクリプト。
    /// 実行後は Tools > UdonSharp > Refresh All UdonSharp Programs を実行すること。
    /// 適用が確認できたら本ファイルは削除可能。
    /// </summary>
    internal static class AddDoubleTriggerRightToggle
    {
        // WallControlPanel.prefab の GUID（.meta ファイルより）
        private const string WALL_PANEL_GUID = "ce00acc11ee08524eb38fdb92ff3c79b";
        // AunCast.prefab の GUID（.meta ファイルより）
        private const string AUNCAST_GUID = "0d617877712537147a584eb2a1ef735c";

        [MenuItem("Tools/AunCast/Add DoubleTrigger Right Toggle")]
        internal static void Execute()
        {
            bool wallOk = PatchWallControlPanelPrefab();
            bool auncOk = PatchAunCastPrefab();

            string status = "[AddDoubleTriggerRightToggle] ";
            status += wallOk ? "WallControlPanel: OK  " : "WallControlPanel: エラー  ";
            status += auncOk ? "AunCast: OK" : "AunCast: エラー";
            Debug.Log(status);
            AssetDatabase.Refresh();
        }

        // ─── WallControlPanel.prefab パッチ ───────────────────────────

        private static bool PatchWallControlPanelPrefab()
        {
            string path = AssetDatabase.GUIDToAssetPath(WALL_PANEL_GUID);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[AddDoubleTriggerRightToggle] WallControlPanel.prefab が見つかりません (GUID: " + WALL_PANEL_GUID + ")");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool ok = PatchWallControlPanelRoot(root);
                if (ok)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                return ok;
            }
            catch (Exception e)
            {
                Debug.LogError("[AddDoubleTriggerRightToggle] WallControlPanel パッチ中にエラー: " + e);
                return false;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool PatchWallControlPanelRoot(GameObject root)
        {
            WallControlPanel panel = root.GetComponentInChildren<WallControlPanel>(true);
            if (panel == null)
            {
                Debug.LogError("[AddDoubleTriggerRightToggle] WallControlPanel コンポーネントが見つかりません");
                return false;
            }

            var panelSo = new SerializedObject(panel);
            var rightProp = panelSo.FindProperty("gestureDoubleTriggerRightToggle");
            if (rightProp != null && rightProp.objectReferenceValue != null)
            {
                Debug.Log("[AddDoubleTriggerRightToggle] gestureDoubleTriggerRightToggle は既に設定済みです。スキップします。");
                return true;
            }

            var leftProp = panelSo.FindProperty("gestureDoubleTriggerToggle");
            if (leftProp == null || leftProp.objectReferenceValue == null)
            {
                Debug.LogError("[AddDoubleTriggerRightToggle] gestureDoubleTriggerToggle が見つかりません");
                return false;
            }

            Toggle leftToggle = leftProp.objectReferenceValue as Toggle;
            if (leftToggle == null) { Debug.LogError("[AddDoubleTriggerRightToggle] Toggle が null です"); return false; }

            GameObject leftGo = leftToggle.gameObject;
            RectTransform vrGroup = leftGo.transform.parent as RectTransform;
            if (vrGroup == null) { Debug.LogError("[AddDoubleTriggerRightToggle] VrGestureGroup が見つかりません"); return false; }

            // 左手ラベルに "L" を付与
            TMP_Text leftLabel = leftGo.GetComponentInChildren<TMP_Text>(true);
            if (leftLabel != null)
                leftLabel.text = "\uf38b L Double-tap Trigger";

            // 左手トグルを複製して右手トグルを作成
            GameObject rightGo = UnityEngine.Object.Instantiate(leftGo, vrGroup);
            rightGo.name = "GestureDoubleTriggerRightToggle";

            // 右手ラベルに "R" を設定
            TMP_Text rightLabel = rightGo.GetComponentInChildren<TMP_Text>(true);
            if (rightLabel != null)
                rightLabel.text = "\uf38b R Double-tap Trigger";

            // 右手トグルの OnValueChanged イベントを OnGestureDoubleTriggerRightToggleChanged に変更
            Toggle rightToggle = rightGo.GetComponent<Toggle>();
            if (rightToggle != null)
            {
                var toggleSo = new SerializedObject(rightToggle);
                var callsPath = "onValueChanged.m_PersistentCalls.m_Calls";
                var callsProp = toggleSo.FindProperty(callsPath);
                if (callsProp != null && callsProp.arraySize > 0)
                {
                    var firstCall = callsProp.GetArrayElementAtIndex(0);
                    var strArg = firstCall.FindPropertyRelative("m_Arguments.m_StringArgument");
                    if (strArg != null)
                        strArg.stringValue = "OnGestureDoubleTriggerRightToggleChanged";
                }
                toggleSo.ApplyModifiedProperties();
            }

            // 右手トグルを左手トグルの直下（y = -72）に配置
            RectTransform rightRect = rightGo.GetComponent<RectTransform>();
            if (rightRect != null)
                rightRect.anchoredPosition = new Vector2(rightRect.anchoredPosition.x, -72f);

            // BothTriggers / RightStickUp を 48px 下へシフト
            for (int i = 0; i < vrGroup.childCount; i++)
            {
                Transform child = vrGroup.GetChild(i);
                if (child.name != "GestureBothTriggersToggle" && child.name != "GestureRightStickUpToggle") continue;
                RectTransform rt = child as RectTransform;
                if (rt == null) continue;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y - 48f);
            }

            // VrGestureGroup の高さを 48px 拡張
            vrGroup.sizeDelta = new Vector2(vrGroup.sizeDelta.x, vrGroup.sizeDelta.y + 48f);

            // WallControlPanel の gestureDoubleTriggerRightToggle を配線
            panelSo.Update();
            var rightPropNew = panelSo.FindProperty("gestureDoubleTriggerRightToggle");
            if (rightPropNew != null)
                rightPropNew.objectReferenceValue = rightToggle;
            panelSo.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(panel);

            return true;
        }

        // ─── AunCast.prefab パッチ ───────────────────────────────────

        private static bool PatchAunCastPrefab()
        {
            string path = AssetDatabase.GUIDToAssetPath(AUNCAST_GUID);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[AddDoubleTriggerRightToggle] AunCast.prefab が見つかりません (GUID: " + AUNCAST_GUID + ")");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool ok = PatchAunCastRoot(root);
                if (ok)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                return ok;
            }
            catch (Exception e)
            {
                Debug.LogError("[AddDoubleTriggerRightToggle] AunCast パッチ中にエラー: " + e);
                return false;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool PatchAunCastRoot(GameObject root)
        {
            UserStatusPanel panel = root.GetComponentInChildren<UserStatusPanel>(true);
            if (panel == null)
            {
                Debug.LogError("[AddDoubleTriggerRightToggle] AunCast.prefab に UserStatusPanel が見つかりません");
                return false;
            }

            var panelSo = new SerializedObject(panel);
            var summonProp = panelSo.FindProperty("summonGesture");
            if (summonProp == null)
            {
                Debug.LogError("[AddDoubleTriggerRightToggle] summonGesture プロパティが見つかりません");
                return false;
            }

            int current = summonProp.intValue;
            int desired = current | UserStatusPanel.GESTURE_DOUBLE_TRIGGER_RIGHT;
            if (current == desired)
            {
                Debug.Log($"[AddDoubleTriggerRightToggle] AunCast.prefab の summonGesture は既に {current} です。スキップします。");
                return true;
            }

            summonProp.intValue = desired;
            panelSo.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(panel);
            Debug.Log($"[AddDoubleTriggerRightToggle] AunCast.prefab の summonGesture を {current} → {desired} に更新しました");
            return true;
        }
    }
}
