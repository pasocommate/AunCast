
using UnityEditor;
using UdonSharpEditor;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// AunCastVideoPlayerManager の Inspector カスタムエディタ。
    /// 配線漏れや参照切れをビルド前に検出できるよう、検証結果を Inspector 上に表示する。
    /// </summary>
    [CustomEditor(typeof(AunCastVideoPlayerManager))]
    internal class AunCastVideoPlayerManagerInspector : Editor
    {
        private SerializedProperty receiverProperty;
        private SerializedProperty avProVideoProperty;
        private SerializedProperty avProRendererProperty;
        private SerializedProperty audioSourcesProperty;

        private void OnEnable()
        {
            receiverProperty = serializedObject.FindProperty(nameof(AunCastVideoPlayerManager.receiver));
            avProVideoProperty = serializedObject.FindProperty(nameof(AunCastVideoPlayerManager.avProPlayer));
            avProRendererProperty = serializedObject.FindProperty(nameof(AunCastVideoPlayerManager.avProTextureRenderer));
            audioSourcesProperty = serializedObject.FindProperty(nameof(AunCastVideoPlayerManager.audioSources));
        }

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawProgramSource(target, false)) return;

            EditorGUILayout.HelpBox(AunCastEditorLocalization.Localize(
                "このゲームオブジェクト上のビデオプレイヤーを直接変更しないでください｡ すべての変更はDualPlayerControllerで行う必要があります｡ これらの設定を変更すると､ 動作が壊れます｡",
                "Do not modify the video player on this GameObject directly. All changes must be made via AunCastDualPlayerController. Changing these settings will break behavior."), MessageType.Warning);
            EditorGUILayout.PropertyField(receiverProperty);
            EditorGUILayout.PropertyField(avProVideoProperty);
            EditorGUILayout.PropertyField(avProRendererProperty);
            EditorGUILayout.PropertyField(audioSourcesProperty, true);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
