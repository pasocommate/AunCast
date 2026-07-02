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
# panel-viewer.md「手元パネルの各部」の表①〜⑧に対応
JOBS = [
    (
        f"{ASSETS}/portable-panel-viewer.png",
        f"{ASSETS}/portable-panel-viewer-annotated.png",
        [
            (1, 1018, 866),  # Resync ボタン
            (2, 114, 866),   # Reboot ボタン（⚡）
            (3, 886, 322),   # Drift
            (4, 886, 404),   # Audio Level
            (5, 76, 704),    # Silence Resync
            (6, 76, 783),    # Volume
            (7, 76, 621),    # 再生ステータス表示（サムネイル下）
            (8, 1372, 68),   # × ボタン
        ],
    ),
    # panel-staff.md「スタッフビュー」の各部①〜⑩に対応
    (
        f"{ASSETS}/portable-panel-staff.png",
        f"{ASSETS}/portable-panel-staff-annotated.png",
        [
            (1, 82, 218),    # Playing（配信中URL）
            (2, 82, 306),    # Next URL 入力欄
            (3, 1342, 266),  # 送信ボタン（↑↓）
            (4, 118, 388),   # Stop All
            (5, 478, 388),   # Reboot All
            (6, 840, 388),   # Resync All
            (7, 855, 510),   # 状態インジケーター
            (8, 1360, 500),  # 人数表示（Playing / Instance / Queued）
            (9, 82, 665),    # Connections / Concurrent の Edit
            (10, 230, 945),  # ヘルプ表示（言語切替）
        ],
    ),
]

# 切り出しジョブ: (元画像, 出力画像, (left, top, right, bottom))
CROPS = [
    # monitoring.md 用: 状態インジケーターと人数表示の拡大
    (
        f"{ASSETS}/portable-panel-staff.png",
        f"{ASSETS}/portable-panel-staff-indicators.png",
        (870, 465, 1395, 745),
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
