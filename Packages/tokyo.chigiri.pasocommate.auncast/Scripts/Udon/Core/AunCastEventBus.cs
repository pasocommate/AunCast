using UdonSharp;
using UnityEngine;
using VRC.Udon;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// シーン内に複数配置される購読者へ、具象型に依存せずイベントを配信するハブ。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastEventBus : UdonSharpBehaviour
    {
        public const string EVENT_VIDEO_TEXTURE_CHANGED = "OnVideoTextureChanged";
        public const string EVENT_LOCAL_STATE_CHANGED = "OnLocalStateChanged";
        public const string EVENT_PORTABLE_PANEL_SHOWN = "OnPortablePanelShown";

        [System.NonSerialized] public Texture videoTexture;
        [System.NonSerialized] public bool videoFlipY;

        [SerializeField] private UdonBehaviour[] videoTextureSubscribers;
        [SerializeField] private UdonBehaviour[] localStateSubscribers;
        [SerializeField] private UdonBehaviour[] portablePanelShownSubscribers;

        public void PublishVideoTexture(Texture tex, bool flipY)
        {
            videoTexture = tex;
            videoFlipY = flipY;
            Broadcast(videoTextureSubscribers, EVENT_VIDEO_TEXTURE_CHANGED);
        }

        public void PublishLocalStateChanged()
        {
            Broadcast(localStateSubscribers, EVENT_LOCAL_STATE_CHANGED);
        }

        public void PublishPortablePanelShown()
        {
            Broadcast(portablePanelShownSubscribers, EVENT_PORTABLE_PANEL_SHOWN);
        }

        private void Broadcast(UdonBehaviour[] subscribers, string eventName)
        {
            if (subscribers == null) return;
            for (int i = 0; i < subscribers.Length; i++)
            {
                UdonBehaviour subscriber = subscribers[i];
                if (subscriber != null)
                    subscriber.SendCustomEvent(eventName);
            }
        }
    }
}
