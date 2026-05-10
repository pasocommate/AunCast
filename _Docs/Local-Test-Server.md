# AunCast デバッグ用 ローカル RTSP サーバ構築手順

AunCast の Resync ロジック・ドリフト検知・スタッフ操作などを、外部 CDN を使わず手元だけで反復検証するための手順をまとめる。Windows 上で MediaMTX (旧 rtsp-simple-server) を立て、FFmpeg からテストパターンまたは既存動画ファイルを擬似ライブ配信する構成を取る。

## 1. なぜ MediaMTX + FFmpeg か

- `Packages/tokyo.chigiri.pasocommate.auncast/_Docs/Design.md` の通り AunCast は `rtspt://` 系（RTSP over TCP）を主用途とする。
- **MediaMTX** (https://github.com/bluenviron/mediamtx) は単一の `mediamtx.exe` だけで RTSP/RTMP/HLS/WebRTC を受信・配信できる Go 製サーバ。インストーラ不要、Windows ZIP リリースあり、MIT ライセンス。
- 既定設定で RTSP 8554/TCP・UDP が開く。AunCast 側の URL 受け入れ実装 (`Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/Core/LocalDualPlayerController.cs` の `PlayVideoAsStaff`) はそのまま使えるためコード変更は不要。
- `rtspt://` で TCP トランスポートを強制する AVPro 形式も、サーバ側は同じ `rtsp://` エンドポイントで受け付ける。

代替候補との比較:

| 候補 | 採否 | 理由 |
|------|------|------|
| Nginx-RTMP | × | RTMP 受信は得意だが RTSP 出力ができず AunCast の主用途と合わない |
| SRS (Simple Realtime Server) | × | 公式 Windows ビルドがなく WSL/Docker 前提 |
| Live555 / GStreamer | × | 設定が重く、即席デバッグ用途には過剰 |

## 2. MediaMTX セットアップ (Windows)

1. https://github.com/bluenviron/mediamtx/releases から `mediamtx_v<バージョン>_windows_amd64.zip` を取得。
2. 任意のフォルダに展開する（例: `C:\tools\mediamtx\`）。同梱物は `mediamtx.exe` と `mediamtx.yml` のみ。
3. 設定はデフォルトで RTSP 8554 が開くため、追加変更は不要。
4. `mediamtx.exe` をダブルクリックで起動。コンソールに以下が出れば OK。
   ```
   [RTSP] listener opened on :8554 (TCP), :8000 (UDP/RTP), :8001 (UDP/RTCP)
   ```
5. 初回起動時に Windows Defender Firewall が許可を求めるため、**プライベートネットワーク**のみチェックして許可。パブリック側は閉じておく。

## 3. FFmpeg による配信ソース

### 3.1 テストパターン送出（カラーバー＋サイン波音声、無限ループ）

```
ffmpeg -re -f lavfi -i "testsrc2=size=1280x720:rate=30" ^
       -f lavfi -i "sine=frequency=1000:sample_rate=48000" ^
       -c:v libx264 -preset ultrafast -tune zerolatency -g 60 ^
       -c:a aac -b:a 128k ^
       -f rtsp -rtsp_transport tcp rtsp://127.0.0.1:8554/test
```

ポイント:
- `-re` で実時間送出（指定しないと最速で送ってバッファが詰まる）
- `-tune zerolatency -g 60` で B フレーム無効・GOP=60 にして低遅延化
- `-rtsp_transport tcp` で MediaMTX への ingest を TCP に固定

### 3.2 既存動画ファイルの擬似ライブ配信（無限ループ）

```
ffmpeg -re -stream_loop -1 -i "C:\path\to\sample.mp4" ^
       -c:v libx264 -preset ultrafast -tune zerolatency -g 60 ^
       -c:a aac -b:a 128k ^
       -f rtsp -rtsp_transport tcp rtsp://127.0.0.1:8554/loop
```

ポイント:
- `-stream_loop -1` で無限ループ
- 元動画のコーデックがそのまま AVPro で再生可能なら `-c:v copy -c:a copy` に置き換えると CPU 負荷が下がる（ただし GOP/CRF が制御できないため遅延特性が変わる点に注意）

### 3.3 同時 2 系統運用

`/test` と `/loop` を別ターミナルで同時に起動しておくと、StaffControlPanel から URL を切り替えての系統間切替テストができる。

## 4. AunCast 側での使い方

1. VRChat クライアントの設定で **Allow Untrusted URLs** を有効化（Settings > Comfort & Safety > Untrusted URLs）。
2. AunCast デモワールドに入り、StaffControlPanel (`Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/UI/StaffControlPanel.cs`) の URL 入力フィールド (`nextUrlField`) に以下を入力。
   - `rtspt://127.0.0.1:8554/test` … TCP 強制（本番に近い形）
   - `rtsp://127.0.0.1:8554/test` … UDP も許容
3. Play ボタンで再生開始。`LocalDualPlayerController` の URL バリデーション（プロトコルスキーム長 1〜8 文字）は `rtsp` (4)・`rtspt` (5) のいずれもクリアする。

> 注意: VRChat クライアントと MediaMTX を同じ PC で動かすことを前提にしている。別 PC から参照する場合は `127.0.0.1` をその PC の LAN IP に置き換え、ファイアウォール 8554/TCP を開放する。

## 5. 検証シナリオ

| シナリオ | 操作 | 期待挙動 |
|---------|------|---------|
| 単発再生 | `/test` を流して URL 入力 → Play | 映像が表示され、`GetTime()` が前進する |
| 停止検知 → 自動 Resync | FFmpeg を Ctrl+C で停止 | 一定時間後に Standby 系で再接続 → 切替 |
| ドリフト/系統切替 | `/test` 再生中に URL を `/loop` に変更 | Standby 先行接続後に Active へ昇格 |
| グローバル Resync | スタッフ操作で Resync を全クライアントに発火 | Coordinator の予約制御に従って順次切替 |
| 二重化動作 | Unity Console で `LocalDualPlayerController` のログを観察 | Active/Standby のロール遷移ログが出る |

## 6. トラブルシュート

| 症状 | 確認ポイント |
|------|-------------|
| VRChat 側で映像が出ない | (1) Allow Untrusted URLs が有効か (2) ファイアウォール (3) URL のタイポ (4) AVPro ログ `%AppData%\..\LocalLow\VRChat\VRChat\output_log_*.txt` |
| 遅延が大きい | `-tune zerolatency`・`-g 30` への調整、必要なら `-rtsp_transport udp` に切替 |
| 音声が出ない | `Docs/VRChat-Udon-Development-Notes.md` の AVPro オーディオ関連項目を参照 |
| MediaMTX が `address already in use` で落ちる | 8554/8000/8001 を別プロセスが使用していないか `netstat -ano | findstr 8554` で確認 |

## 7. 補助バッチ例（コピペ用）

`start-mediamtx.bat`:
```
@echo off
cd /d C:\tools\mediamtx
mediamtx.exe
```

`stream-testpattern.bat`:
```
@echo off
ffmpeg -re -f lavfi -i "testsrc2=size=1280x720:rate=30" ^
       -f lavfi -i "sine=frequency=1000:sample_rate=48000" ^
       -c:v libx264 -preset ultrafast -tune zerolatency -g 60 ^
       -c:a aac -b:a 128k ^
       -f rtsp -rtsp_transport tcp rtsp://127.0.0.1:8554/test
pause
```

`stream-loopfile.bat` (`%1` に動画パスを渡す):
```
@echo off
ffmpeg -re -stream_loop -1 -i "%~1" ^
       -c:v libx264 -preset ultrafast -tune zerolatency -g 60 ^
       -c:a aac -b:a 128k ^
       -f rtsp -rtsp_transport tcp rtsp://127.0.0.1:8554/loop
pause
```
