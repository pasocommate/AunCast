#if UNITY_EDITOR
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRC.Udon;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// NowPlayingArea の隣に「再生中URLを入力欄に戻す」ボタンを追加するパッチ。
    /// メニューから一度だけ実行し、完了後にこのスクリプトは削除してよい。
    /// </summary>
    public static class AddCopyPlayingUrlButton
    {
        [MenuItem("Tools/AunCast/Add CopyPlayingUrl Button")]
        public static void Execute()
        {
            // NowPlayingArea を基準に探す
            var staffPanel = Object.FindObjectOfType<StaffControlPanel>(true);
            if (staffPanel == null)
            {
                Debug.LogError("[AunCast] StaffControlPanel が見つかりません。シーンに AunCast プレハブを配置してください。");
                return;
            }

            var staffPanelTransform = staffPanel.transform;
            var nowPlayingArea = FindChildRecursive(staffPanelTransform, "NowPlayingArea");
            if (nowPlayingArea == null)
            {
                Debug.LogError("[AunCast] NowPlayingArea が見つかりません。");
                return;
            }

            // 既に存在する場合はスキップ
            if (nowPlayingArea.parent.Find("CopyPlayingUrlButton") != null)
            {
                Debug.LogWarning("[AunCast] CopyPlayingUrlButton は既に存在します。");
                return;
            }

            // PromoteNextButton をテンプレートとして見つける
            var promoteButton = FindChildRecursive(staffPanelTransform, "PromoteNextButton");
            if (promoteButton == null)
            {
                Debug.LogError("[AunCast] PromoteNextButton が見つかりません。テンプレートなしでは作成できません。");
                return;
            }

            // UdonBehaviour を取得（イベントターゲット）
            UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(staffPanel);
            if (udon == null)
            {
                Debug.LogError("[AunCast] StaffControlPanel の UdonBehaviour が見つかりません。");
                return;
            }

            // PromoteNextButton を複製
            GameObject clone = Object.Instantiate(promoteButton.gameObject, nowPlayingArea.parent, false);
            Undo.RegisterCreatedObjectUndo(clone, "Add CopyPlayingUrl Button");
            clone.name = "CopyPlayingUrlButton";

            // NowPlayingArea の直後に配置
            int nowPlayingIndex = nowPlayingArea.GetSiblingIndex();
            clone.transform.SetSiblingIndex(nowPlayingIndex + 1);

            // RectTransform: NowPlayingArea の右端に配置
            var rt = clone.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(0, -24);
            rt.sizeDelta = new Vector2(50, 48);

            // NowPlayingArea の幅を少し縮めてボタン分のスペースを確保
            var nowPlayingRt = nowPlayingArea.GetComponent<RectTransform>();
            nowPlayingRt.sizeDelta = new Vector2(nowPlayingRt.sizeDelta.x - 55, nowPlayingRt.sizeDelta.y);

            // _Inner の Button の onClick を差し替え
            var inner = clone.transform.GetChild(0);
            var button = inner.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                var entry = new UnityEngine.Events.UnityAction(delegate { });
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(
                    button.onClick,
                    new UnityEngine.Events.UnityAction<string>(udon.SendCustomEvent),
                    "OnCopyPlayingUrlToInput"
                );
            }

            // EventTrigger のホバーイベントを差し替え
            var eventTrigger = inner.GetComponent<EventTrigger>();
            if (eventTrigger != null)
            {
                eventTrigger.triggers.Clear();

                // PointerEnter → OnHoverCopyPlayingUrl
                var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(
                    enterEntry.callback,
                    new UnityEngine.Events.UnityAction<string>(udon.SendCustomEvent),
                    "OnHoverCopyPlayingUrl"
                );
                eventTrigger.triggers.Add(enterEntry);

                // PointerExit → OnHoverClear
                var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(
                    exitEntry.callback,
                    new UnityEngine.Events.UnityAction<string>(udon.SendCustomEvent),
                    "OnHoverClear"
                );
                eventTrigger.triggers.Add(exitEntry);
            }

            // 背景色を NowPlayingArea と同系色に（青系）
            var bgImage = inner.GetComponent<Image>();
            if (bgImage != null)
                bgImage.color = new Color(0.2f, 0.5f, 0.8f, 1f);

            // アイコンテキストを content_copy に変更
            var label = inner.Find("Label");
            if (label != null)
            {
                var tmp = label.GetComponent<TMP_Text>();
                if (tmp != null)
                    tmp.text = MaterialIcons.ContentCopy;
            }

            EditorUtility.SetDirty(clone);
            PrefabUtility.RecordPrefabInstancePropertyModifications(clone);
            Debug.Log("[AunCast] CopyPlayingUrlButton を NowPlayingArea の隣に追加しました。プレハブに Apply してください。");
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
