# マニュアル用スクリーンショットに番号バッジを合成するスクリプト。
# 元画像を撮り直した際は、このスクリプトを再実行すれば注記付き画像を再生成できる。
#   実行: python Tools/annotate-manual-screenshots.py
# 座標は元画像のピクセル座標（左上原点）。レイアウトが変わったら badges を調整する。

from PIL import Image, ImageDraw, ImageFont

ASSETS = "Manual/src/assets"

# バッジの見た目（本文の図と揃えた紫系）
BADGE_RADIUS = 36
BADGE_FILL = (90, 74, 168, 255)      # #5a4aa8
BADGE_STROKE = (255, 255, 255, 255)  # 白フチ
BADGE_STROKE_W = 4
FONT_PATH = r"C:\Windows\Fonts\arialbd.ttf"
FONT_SIZE = 46

# ジョブ定義: (元画像, 出力画像, [(番号, x, y), ...])
# panel-viewer.md「手元パネルの各部」の表①〜⑩に対応
JOBS = [
    (
        f"{ASSETS}/portable-panel-viewer.png",
        f"{ASSETS}/portable-panel-viewer-annotated.png",
        [
            (1, 1372, 940),  # Resync ボタンの右側
            (2, 65, 940),    # Reboot ボタンの左側
            (3, 885, 326),   # Drift の左側
            (4, 885, 415),   # Audio Level の左側
            (5, 66, 730),    # Silence Resync の左側
            (6, 550, 670),   # Mode の上側
            (7, 66, 812),    # Volume の左側
            (8, 66, 614),    # 再生ステータス表示の左側
            (9, 1380, 172),  # × ボタンの右下
            (10, 1145, 92),  # ビュー切替ボタンの左側
        ],
    ),
    # panel-staff.md「スタッフビュー」の各部①〜⑯に対応
    (
        f"{ASSETS}/portable-panel-staff.png",
        f"{ASSETS}/portable-panel-staff-annotated.png",
        [
            (1, 65, 199),    # Playing の左側
            (2, 65, 291),    # Next URL の左側
            (3, 1372, 290),  # 送信ボタンの右側
            (4, 65, 405),    # Stop All の左側
            (5, 640, 480),   # Reboot All の下側
            (6, 1372, 465),  # Resync All の右側
            (7, 870, 465),   # 状態インジケーターの上側
            (8, 1372, 650),  # 人数表示の右側
            (9, 745, 630),   # Connection / Concurrent の Edit の左側
            (10, 745, 735),  # Drift Threshold の Edit の左側
            (11, 745, 805),  # Force Mode の Edit の左側
            (12, 1100, 850), # ヘルプ表示の上側
            (13, 1372, 940), # Resync ボタン（下部・観客ビューと同じ）の右側
            (14, 65, 940),   # Reboot ボタン（⚡・下部・観客ビューと同じ）の左側
            (15, 1040, 92),  # スタッフ操作ロックボタンの左側
            (16, 65, 526),   # Timeline Log の左側
        ],
    ),
]

# 切り出しジョブ: (元画像, 出力画像, (left, top, right, bottom))
CROPS = [
    # monitoring.md 用: 状態インジケーターと人数表示の拡大
    (
        f"{ASSETS}/portable-panel-staff.png",
        f"{ASSETS}/portable-panel-staff-indicators.png",
        (860, 470, 1395, 780),
    ),
    # panel-wall.md 用: 呼び出し設定の拡大
    (
        f"{ASSETS}/wall-panel-user-desktop.png",
        f"{ASSETS}/wall-panel-spawn-gesture-desktop.png",
        (120, 390, 960, 742),
    ),
    (
        f"{ASSETS}/wall-panel-user-vr.png",
        f"{ASSETS}/wall-panel-spawn-gesture-vr.png",
        (120, 390, 960, 782),
    ),
]


def annotate(src_path, dst_path, badges):
    im = Image.open(src_path).convert("RGBA")
    # 高解像度で描いて縮小するとバッジの縁が滑らかになる
    scale = 4
    overlay = Image.new("RGBA", (im.width * scale, im.height * scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    font = ImageFont.truetype(FONT_PATH, FONT_SIZE * scale)
    r = BADGE_RADIUS * scale
    for num, x, y in badges:
        cx, cy = x * scale, y * scale
        draw.ellipse(
            (cx - r, cy - r, cx + r, cy + r),
            fill=BADGE_FILL, outline=BADGE_STROKE, width=BADGE_STROKE_W * scale,
        )
        draw.text((cx, cy), str(num), font=font, fill=(255, 255, 255, 255), anchor="mm")
    overlay = overlay.resize(im.size, Image.LANCZOS)
    im.alpha_composite(overlay)
    im.save(dst_path)
    print(f"{dst_path} を生成しました（バッジ {len(badges)} 個）")


def crop(src_path, dst_path, box):
    im = Image.open(src_path)
    im.crop(box).save(dst_path)
    print(f"{dst_path} を切り出しました {box}")


if __name__ == "__main__":
    for src, dst, badges in JOBS:
        annotate(src, dst, badges)
    for src, dst, box in CROPS:
        crop(src, dst, box)
