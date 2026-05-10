using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Video.Components.Base;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// A/B 両プレイヤーの再生時刻ドリフト（プレイヤー時刻 vs サーバー時刻の差）を
    /// リアルタイムにスクロールグラフとして可視化する開発用デバッグツール。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SyncDebugDisplay : UdonSharpBehaviour
    {
        [Header("Video Players")]
        [SerializeField] BaseVRCVideoPlayer playerA;
        [SerializeField] BaseVRCVideoPlayer playerB;

        [Header("Audio Sources")]
        [SerializeField] AudioSource audioSourceA;
        [SerializeField] AudioSource audioSourceB;

        [Header("Controller")]
        [SerializeField] LocalDualPlayerController controller;

        [Header("Silence Detectors")]
        [SerializeField] AudioSilenceDetector silenceDetectorA;
        [SerializeField] AudioSilenceDetector silenceDetectorB;

        [Header("Text Displays")]
        [SerializeField] TextMeshProUGUI textDisplayA;
        [SerializeField] TextMeshProUGUI textDisplayB;

        [Header("Graph")]
        [SerializeField] RenderTexture renderTexture;
        [SerializeField] int graphWidth  = 512;
        [SerializeField] int graphHeight = 512;
        // グラフの縦軸範囲: rate - 1.0 の値。例 0.5 なら -0.5〜+0.5
        [SerializeField] float rateRange = 0.5f;

        Texture2D _tex;
        Color32[] _pixels;
        /// <summary>循環バッファグラフの現在の書き込み位置。</summary>
        int _writeHead = 0;
        /// <summary>グラフ高さの半分。A/B 各スロットがこの高さを使う。</summary>
        int _halfHeight;

        // スロット別の状態 [0]=A(上半分), [1]=B(下半分)
        /// <summary>前フレームの実時間。デルタ計算用。</summary>
        float[] _prevWallTime;
        float[] _prevPlayerTime;
        float[] _prevServerTime;
        /// <summary>再生開始時点のプレイヤー時刻。ギャップ算出の基準点。</summary>
        float[] _playStartPlayerTime;
        /// <summary>再生開始時点のサーバー時刻。期待再生位置の算出に使う。</summary>
        float[] _playStartServerTime;
        bool[]  _hasPlayStartServerTime;

        // UpdateSlot の結果受け渡し用
        float _slotVal;
        bool  _slotHasVal;

        // グラフ描画色: 正常時は緑、ストール/大きなズレ時は赤で視覚的に異常を判別できる
        readonly Color32 _colorBg      = new Color32( 10,  10,  30, 255); // 背景
        readonly Color32 _colorZero    = new Color32( 60,  60, 120, 255); // ゼロライン（ドリフトなし）
        readonly Color32 _colorNormal  = new Color32( 40, 160,  80, 255); // 正常再生（ドリフト小）
        readonly Color32 _colorStall   = new Color32(200,  60,  60, 255); // 異常（ドリフト大/ストール）
        readonly Color32 _colorHead    = new Color32( 80,  80,  80, 255); // 書き込みヘッド位置表示
        readonly Color32 _colorDivider = new Color32(120, 120, 140, 255); // A/B 境界線

        void Start()
        {
            _halfHeight = graphHeight / 2;

            _prevWallTime           = new float[] { -1f, -1f };
            _prevPlayerTime         = new float[2];
            _prevServerTime         = new float[2];
            _playStartPlayerTime    = new float[2];
            _playStartServerTime    = new float[2];
            _hasPlayStartServerTime = new bool[2];

            _tex = new Texture2D(graphWidth, graphHeight, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Point;
            _pixels = new Color32[graphWidth * graphHeight];
            for (int i = 0; i < _pixels.Length; i++)
                _pixels[i] = _colorBg;

            DrawZeroLine(0);
            DrawZeroLine(1);
            DrawDividerLine();

            _tex.SetPixels32(_pixels);
            _tex.Apply();
            if (renderTexture != null)
                VRCGraphics.Blit(_tex, renderTexture);
        }

        /// <summary>全 Update 完了後に実行し、フレーム末の確定状態でグラフを更新する。</summary>
        public override void PostLateUpdate()
        {
            float serverTime = (float)((double)Networking.GetServerTimeInMilliseconds() / 1000.0);
            float wallTime = Time.realtimeSinceStartup;

            UpdateSlot(0, playerA, audioSourceA, silenceDetectorA, textDisplayA, serverTime, wallTime);
            bool hasA = _slotHasVal;
            float valA = _slotVal;

            UpdateSlot(1, playerB, audioSourceB, silenceDetectorB, textDisplayB, serverTime, wallTime);
            bool hasB = _slotHasVal;
            float valB = _slotVal;

            if ((!hasA && !hasB) || renderTexture == null || _tex == null) return;

            int nextHead = (_writeHead + 1) % graphWidth;

            if (hasA) RedrawColumn(_writeHead, valA, false, 0);
            else      RedrawColumn(_writeHead, 0f,  true,  0);
            if (hasB) RedrawColumn(_writeHead, valB, false, 1);
            else      RedrawColumn(_writeHead, 0f,  true,  1);

            RedrawColumn(nextHead, 0f, true, 0);
            RedrawColumn(nextHead, 0f, true, 1);

            // 境界線を復元（列の描画で上書きされるため）
            _pixels[_halfHeight * graphWidth + _writeHead] = _colorDivider;
            _pixels[_halfHeight * graphWidth + nextHead]   = _colorDivider;

            _tex.SetPixels32(_pixels);
            _tex.Apply();
            VRCGraphics.Blit(_tex, renderTexture);

            _writeHead = nextHead;
        }

        /// <summary>
        /// プレイヤー時刻とサーバー経過時刻のギャップを算出し、テキスト表示を更新する。
        /// 結果は _slotVal/_slotHasVal にセットしてグラフ描画に渡す。
        /// </summary>
        void UpdateSlot(int slot, BaseVRCVideoPlayer player, AudioSource audioSource,
            AudioSilenceDetector silenceDetector, TextMeshProUGUI textDisplay, float serverTime, float wallTime)
        {
            _slotHasVal = false;
            _slotVal = 0f;

            if (player == null) return;

            bool isPlaying = player.IsPlaying;
            float playerTime = player.GetTime();

            if (!isPlaying)
            {
                _hasPlayStartServerTime[slot] = false;
                _prevWallTime[slot] = -1f;
            }
            else if (!_hasPlayStartServerTime[slot] && playerTime > 0f)
            {
                _playStartPlayerTime[slot] = playerTime;
                _playStartServerTime[slot] = serverTime;
                _hasPlayStartServerTime[slot] = true;
                _prevWallTime[slot] = -1f;
            }

            float serverTimeFromPlayStart = 0f;
            float currentGap = 0f;
            bool hasGap = false;

            if (isPlaying && _hasPlayStartServerTime[slot] && _prevWallTime[slot] >= 0f)
            {
                serverTimeFromPlayStart = serverTime - _playStartServerTime[slot]
                                          + _playStartPlayerTime[slot];
                currentGap = playerTime - serverTimeFromPlayStart;
                hasGap = true;
            }

            if (textDisplay != null)
            {
                string gapText = hasGap
                    ? $"{currentGap,8:F3}" : "    -";
                string serverFromPlayStartText = hasGap
                    ? $"{serverTimeFromPlayStart,8:F3}" : "    -";
                string volumeText = "    -";
                if (audioSource != null)
                {
                    volumeText = audioSource.mute ? "MUTED" : audioSource.volume.ToString("F3");
                }

                string standbyText = "    -";
                if (controller != null)
                {
                    bool sbReady = controller.GetStandbyReady();
                    bool sbPlay = controller.GetStandbyPlayStarted();
                    int state = controller.GetLocalState();
                    if (state == LocalDualPlayerController.STATE_STANDBY_CONNECTING
                        || state == LocalDualPlayerController.STATE_STANDBY_VERIFYING)
                        standbyText = (sbReady ? "R" : ".") + (sbPlay ? "P" : ".");
                    else
                        standbyText = "IDLE";
                }

                string rmsText = "    -";
                if (silenceDetector != null)
                {
                    float lastRms = silenceDetector.GetLastRms();
                    int sampleCount = silenceDetector.GetLastRmsSampleCount();
                    rmsText = $"{lastRms:F5} N={sampleCount}";
                }

                textDisplay.text =
                    $"VOLUME:    {volumeText}\n" +
                    $"S.TIME: {serverFromPlayStartText}\n" +
                    $"   GAP: {gapText}\n" +
                    $"STD.BY: {standbyText}\n" +
                    $"   RMS: {rmsText}";
            }

            if (isPlaying && hasGap)
            {
                _slotVal = currentGap - Mathf.Round(currentGap);
                _slotHasVal = true;
            }

            _prevWallTime[slot]   = wallTime;
            _prevPlayerTime[slot] = playerTime;
            _prevServerTime[slot] = serverTime;
        }

        // slot: 0 = A (上半分), 1 = B (下半分)
        int SlotYBase(int slot) => slot == 0 ? _halfHeight : 0;

        /// <summary>スクロールグラフの1列を描画する。ドット+背景で構成し、ヘッド位置はグレーで塗る。</summary>
        void RedrawColumn(int x, float val, bool isHead, int slot)
        {
            int yBase = SlotYBase(slot);

            if (isHead)
            {
                for (int y = 0; y < _halfHeight; y++)
                    _pixels[(yBase + y) * graphWidth + x] = _colorHead;
                return;
            }

            for (int y = 0; y < _halfHeight; y++)
                _pixels[(yBase + y) * graphWidth + x] = _colorBg;

            int zeroY = yBase + _halfHeight / 2;
            SetPixel(x, zeroY, _colorZero);

            float t = Mathf.Clamp01(1f - Mathf.Abs(val) / rateRange);
            Color32 dotColor = Color32.Lerp(_colorStall, _colorNormal, t);

            int dotY = ValueToY(val, slot);
            SetPixel(x, dotY, dotColor);
            if (dotY - 1 >= yBase)
                SetPixel(x, dotY - 1, dotColor);
            if (dotY + 1 < yBase + _halfHeight)
                SetPixel(x, dotY + 1, dotColor);
        }

        void DrawZeroLine(int slot)
        {
            int zeroY = SlotYBase(slot) + _halfHeight / 2;
            for (int x = 0; x < graphWidth; x++)
                _pixels[zeroY * graphWidth + x] = _colorZero;
        }

        void DrawDividerLine()
        {
            for (int x = 0; x < graphWidth; x++)
                _pixels[_halfHeight * graphWidth + x] = _colorDivider;
        }

        void SetPixel(int x, int y, Color32 color)
        {
            if (y < 0 || y >= graphHeight) return;
            _pixels[y * graphWidth + x] = color;
        }

        /// <summary>ドリフト値をピクセル Y 座標に変換する。rateRange 内を各スロットの高さにマッピング。</summary>
        int ValueToY(float rateDeviation, int slot)
        {
            int yBase = SlotYBase(slot);
            float t = (rateDeviation + rateRange) / (2f * rateRange);
            return Mathf.Clamp((int)(t * (_halfHeight - 1)) + yBase, yBase, yBase + _halfHeight - 1);
        }
    }
}
