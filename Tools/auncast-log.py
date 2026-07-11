#!/usr/bin/env python3
"""AunCast 常時出力タイムラインログの CLI 解析ツール。

VRChat の output_log から [AunCast:TL] 行と OnVideoError 行を抽出し、
サブコマンドごとに集計して簡潔に表示する。

使い方:
    python Tools/auncast-log.py list              # ログファイル一覧
    python Tools/auncast-log.py summary           # セッション概要
    python Tools/auncast-log.py startup           # 再生開始計測 (MEAS_*) の統計
    python Tools/auncast-log.py resync            # Resync サイクル一覧と所要時間
    python Tools/auncast-log.py events            # 重要イベントの時系列
    python Tools/auncast-log.py events --all      # SNAPSHOT 以外の全 TL 行

対象ログは省略時に VRChat の最新 output_log を自動選択する。
--log PATH で任意のファイル、--dir DIR で検索ディレクトリを指定できる。
"""

import argparse
import glob
import os
import re
import statistics
import sys

DEFAULT_LOG_DIR = os.path.join(
    os.path.expanduser("~"), "AppData", "LocalLow", "VRChat", "VRChat")

# 行頭の VRChat ログタイムスタンプ（例: 2026.07.11 12:34:56 Debug      -  ...）
WALL_RE = re.compile(r"^(\d{4}\.\d{2}\.\d{2}) (\d{2}:\d{2}:\d{2})")
TL_RE = re.compile(r"\[AunCast:TL\] st=(\d+) c=(\S+) (.*)")
VIDEO_ERROR_RE = re.compile(
    r"\[AunCast/AunCastVideoPlayerManager\[(\d)\]\] OnVideoError: (\S+)")
# key=value 抽出（value は次の key= 直前まで。displayName 等の空白入り値に対応）
KV_KEY_RE = re.compile(r"(\w+)=")

# events サブコマンドの既定表示対象（常時出力される重要イベント）
IMPORTANT_ACTIONS = {
    # Resync ライフサイクル (RCC)
    "RESYNC_REQUEST", "RESYNC_GRANTED", "ADOPTION", "RESYNC_CANCEL",
    "RESYNC_RESULT", "RESYNC_RETRY", "REPORT_RUNNING", "SLOT_ASSIGNED",
    "GLOBAL_FORCE_REBOOT",
    # 監視 (APM)
    "INIT_ACTIVE", "FIRST_ADVANCE", "STALL_START", "DRIFT_THRESHOLD",
    "STABLE_PLAYBACK",
    # 切替 (PBS)
    "AUDIO_ONLY_FALLBACK",
    # Coordinator (RC)
    "GLOBAL_RESYNC", "GLOBAL_FORCE_REBOOT_TRIGGER",
    # Controller (LDPC)
    "SLOT_PENDING_LOCAL_PLAYBACK",
    "MEAS_LOAD", "MEAS_READY", "MEAS_START",
    "MEAS_SWITCH_START", "MEAS_SWITCH_DONE",
    # 合成イベント（OnVideoError 行から生成）
    "VIDEO_ERROR",
}


class Event:
    """1 行分の解析結果。st はサーバ時刻 ms（欠落時は None）。"""

    __slots__ = ("line_no", "wall", "st", "comp", "action", "data")

    def __init__(self, line_no, wall, st, comp, action, data):
        self.line_no = line_no
        self.wall = wall          # "HH:MM:SS" または None
        self.st = st
        self.comp = comp
        self.action = action      # a= / e= の値。無い行は None
        self.data = data          # key=value の dict


def parse_kv(text):
    """'k1=v1 k2=v with space k3=v3' 形式を dict にする。"""
    result = {}
    matches = list(KV_KEY_RE.finditer(text))
    for i, m in enumerate(matches):
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        result[m.group(1)] = text[m.end():end].strip()
    return result


def parse_log(path):
    events = []
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for line_no, line in enumerate(f, 1):
            wall_m = WALL_RE.match(line)
            wall = wall_m.group(2) if wall_m else None

            tl = TL_RE.search(line)
            if tl:
                data = parse_kv(tl.group(3))
                action = data.get("a") or data.get("e")
                events.append(Event(
                    line_no, wall, int(tl.group(1)), tl.group(2), action, data))
                continue

            err = VIDEO_ERROR_RE.search(line)
            if err:
                events.append(Event(
                    line_no, wall, None, "VPM", "VIDEO_ERROR",
                    {"p": err.group(1), "err": err.group(2)}))
    return events


def find_log_files(log_dir):
    files = glob.glob(os.path.join(log_dir, "output_log_*.txt"))
    return sorted(files, key=os.path.getmtime)


def resolve_log_path(args):
    if args.log:
        if not os.path.isfile(args.log):
            sys.exit(f"ログファイルが見つかりません: {args.log}")
        return args.log
    files = find_log_files(args.dir)
    if not files:
        sys.exit(f"output_log_*.txt が見つかりません: {args.dir}")
    return files[-1]


def fmt_float(value, digits=3):
    return f"{value:.{digits}f}" if value is not None else "-"


def duration_sec(st_from, st_to):
    if st_from is None or st_to is None:
        return None
    return (st_to - st_from) / 1000.0


def percentile(sorted_values, p):
    """線形補間パーセンタイル。sorted_values は昇順前提。"""
    if not sorted_values:
        return None
    if len(sorted_values) == 1:
        return sorted_values[0]
    k = (len(sorted_values) - 1) * p
    lo = int(k)
    hi = min(lo + 1, len(sorted_values) - 1)
    return sorted_values[lo] + (sorted_values[hi] - sorted_values[lo]) * (k - lo)


def print_stats_row(label, values, unit="s"):
    """1 指標の統計を 1 行で表示する。"""
    if not values:
        print(f"  {label:<14} データなし")
        return
    vs = sorted(values)
    mean = statistics.fmean(vs)
    std = statistics.pstdev(vs) if len(vs) > 1 else 0.0
    print(f"  {label:<14} n={len(vs):<3d} "
          f"min={vs[0]:.3f} p50={percentile(vs, 0.5):.3f} "
          f"mean={mean:.3f} p90={percentile(vs, 0.9):.3f} "
          f"max={vs[-1]:.3f} 幅={vs[-1] - vs[0]:.3f} σ={std:.3f} [{unit}]")


# =====================================================================
#  サブコマンド
# =====================================================================

def cmd_list(args):
    files = find_log_files(args.dir)
    if not files:
        sys.exit(f"output_log_*.txt が見つかりません: {args.dir}")
    latest = files[-1]
    for path in files:
        size_mb = os.path.getsize(path) / (1024 * 1024)
        mark = " <- 最新（既定の解析対象）" if path == latest else ""
        print(f"{os.path.basename(path):<45} {size_mb:6.1f} MB{mark}")


def cmd_summary(args, events, path):
    print(f"ログ: {path}")
    if not events:
        print("AunCast のタイムラインログ行が見つかりませんでした。")
        return

    walls = [e.wall for e in events if e.wall]
    if walls:
        print(f"期間: {walls[0]} 〜 {walls[-1]}")

    for e in events:
        if e.action == "CLIENT_INIT":
            print(f"クライアント: {e.data.get('name', '?')} "
                  f"(playerId={e.data.get('playerId', '?')}, "
                  f"slot={e.data.get('slot', '?')})")

    def count(action):
        return sum(1 for e in events if e.action == action)

    results = [e for e in events if e.action == "RESYNC_RESULT"]
    ok = sum(1 for e in results if e.data.get("success") == "1")
    print()
    print(f"Resync 要求      : {count('RESYNC_REQUEST') + count('ADOPTION')} 件 "
          f"(結果報告 {len(results)} 件: 成功 {ok} / 失敗 {len(results) - ok}, "
          f"キャンセル {count('RESYNC_CANCEL')} 件)")
    print(f"停滞開始         : {count('STALL_START')} 件")
    print(f"ドリフト閾値超過 : {count('DRIFT_THRESHOLD')} 件")
    print(f"映像エラー       : {count('VIDEO_ERROR')} 件")
    print(f"音声フォールバック: {count('AUDIO_ONLY_FALLBACK')} 件")
    globals_count = (count("GLOBAL_RESYNC") + count("GLOBAL_FORCE_REBOOT")
                     + count("GLOBAL_FORCE_REBOOT_TRIGGER"))
    print(f"グローバル指令   : {globals_count} 件")

    load2start = collect_meas(events, "MEAS_START", "load2start")
    if load2start:
        vs = sorted(load2start)
        print()
        print(f"再生開始所要 (load2start): n={len(vs)} "
              f"p50={percentile(vs, 0.5):.3f}s 幅={vs[-1] - vs[0]:.3f}s "
              f"（詳細は startup サブコマンド）")


def collect_meas(events, action, key):
    values = []
    for e in events:
        if e.action != action:
            continue
        try:
            v = float(e.data.get(key, ""))
        except ValueError:
            continue
        if v >= 0:  # -1 は起点未計測のセンチネル
            values.append(v)
    return values


def cmd_startup(args, events, path):
    print(f"ログ: {path}")
    starts = [e for e in events if e.action == "MEAS_START"]
    if not starts:
        print("MEAS_START が見つかりません（再生開始が記録されていません）。")
        return

    print(f"再生開始イベント: {len(starts)} 件")
    print()
    print("接続〜再生開始の所要時間分布:")
    print_stats_row("load2ready", collect_meas(events, "MEAS_READY", "load2ready"))
    print_stats_row("load2start", collect_meas(events, "MEAS_START", "load2start"))
    print_stats_row("ready2start", collect_meas(events, "MEAS_START", "ready2start"))
    print()
    print("イベント時フレーム時間 (dt) / 開始時再生位置 (t):")
    print_stats_row("dt(START)", collect_meas(events, "MEAS_START", "dt"), unit="ms")
    print_stats_row("t(START)", collect_meas(events, "MEAS_START", "t"))
    print()
    print("判定のめやす: load2start の「幅」が配信 GOP 長に近ければキーフレーム境界")
    print("待ちが支配項。dt が大きい試行だけ所要が伸びるならフレームヒッチ起因を疑う。")

    if args.verbose:
        print()
        print("個別サンプル:")
        print(f"  {'時刻':<9} {'p':<2} {'load2start':>10} {'ready2start':>11} "
              f"{'t':>7} {'dt(ms)':>7}")
        for e in starts:
            print(f"  {e.wall or '-':<9} {e.data.get('p', '?'):<2} "
                  f"{e.data.get('load2start', '-'):>10} "
                  f"{e.data.get('ready2start', '-'):>11} "
                  f"{e.data.get('t', '-'):>7} {e.data.get('dt', '-'):>7}")


def cmd_resync(args, events, path):
    print(f"ログ: {path}")
    cycles = build_resync_cycles(events)
    if not cycles:
        print("Resync サイクルが見つかりません。")
        return

    print(f"Resync サイクル: {len(cycles)} 件")
    print()
    print(f"  {'#':>2} {'開始':<9} {'契機':<9} {'待ち':>7} {'接続':>7} "
          f"{'切替':>7} {'合計':>7} 結果")
    durations = []
    for i, c in enumerate(cycles, 1):
        wait = duration_sec(c.get("req"), c.get("grant"))
        connect = duration_sec(c.get("run"), c.get("switch_start"))
        switch = duration_sec(c.get("switch_start"), c.get("switch_done"))
        total = duration_sec(c.get("req"), c.get("end"))
        if total is not None:
            durations.append(total)
        print(f"  {i:>2} {c.get('wall') or '-':<9} {c.get('reason', '-'):<9} "
              f"{fmt_float(wait):>7} {fmt_float(connect):>7} "
              f"{fmt_float(switch):>7} {fmt_float(total):>7} {c.get('result', '未完')}")
    print()
    print("  待ち=要求→Grant / 接続=Standby接続→切替開始 / 単位=秒")
    if durations:
        print_stats_row("合計所要", durations)


def build_resync_cycles(events):
    cycles = []
    current = None
    for e in events:
        if e.action in ("RESYNC_REQUEST", "ADOPTION"):
            if current is not None:
                cycles.append(current)
            current = {
                "req": e.st, "wall": e.wall,
                "reason": e.data.get("reason", "adoption"
                                     if e.action == "ADOPTION" else "-"),
            }
            if e.action == "ADOPTION" and e.data.get("coordState") == "2":
                current["grant"] = e.st  # GRANTED 状態での採用は即 Grant 扱い
        elif current is None:
            continue
        elif e.action == "RESYNC_GRANTED":
            current["grant"] = e.st
        elif e.action == "REPORT_RUNNING":
            current["run"] = e.st
        elif e.action == "MEAS_SWITCH_START":
            current["switch_start"] = e.st
        elif e.action == "MEAS_SWITCH_DONE":
            current["switch_done"] = e.st
        elif e.action == "RESYNC_RESULT":
            current["result"] = ("成功" if e.data.get("success") == "1" else "失敗")
            current["end"] = e.st
            cycles.append(current)
            current = None
        elif e.action == "RESYNC_CANCEL":
            current["result"] = "キャンセル"
            current["end"] = e.st
            cycles.append(current)
            current = None
    if current is not None:
        cycles.append(current)
    return cycles


def cmd_events(args, events, path):
    print(f"ログ: {path}")
    base_st = next((e.st for e in events if e.st is not None), None)
    shown = 0
    for e in events:
        if e.action == "SNAPSHOT":
            continue
        if not args.all and e.action not in IMPORTANT_ACTIONS \
                and e.action != "CLIENT_INIT":
            continue
        rel = f"+{(e.st - base_st) / 1000.0:9.1f}s" if (
            e.st is not None and base_st is not None) else " " * 11
        detail = " ".join(f"{k}={v}" for k, v in e.data.items()
                          if k not in ("a", "e"))
        print(f"{e.wall or '-':<9} {rel} {e.comp:<4} {e.action or '-':<24} {detail}")
        shown += 1
    if shown == 0:
        print("該当イベントなし。")


# =====================================================================
#  エントリポイント
# =====================================================================

def main():
    # リダイレクト時など stdout が日本語を扱えないエンコーディングでも
    # UnicodeEncodeError で落ちないようにする
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="replace")

    parser = argparse.ArgumentParser(
        description="AunCast タイムラインログの CLI 解析ツール",
        formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--log", help="解析するログファイル（省略時は最新の output_log）")
    parser.add_argument("--dir", default=DEFAULT_LOG_DIR,
                        help=f"ログ検索ディレクトリ（既定: {DEFAULT_LOG_DIR}）")
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("list", help="ログファイル一覧を表示する")
    sub.add_parser("summary", help="セッション概要を表示する")
    p_startup = sub.add_parser("startup", help="再生開始計測 (MEAS_*) の統計を表示する")
    p_startup.add_argument("-v", "--verbose", action="store_true",
                           help="個別サンプルも表示する")
    sub.add_parser("resync", help="Resync サイクルの一覧と所要時間を表示する")
    p_events = sub.add_parser("events", help="重要イベントの時系列を表示する")
    p_events.add_argument("--all", action="store_true",
                          help="SNAPSHOT 以外の全 TL 行を表示する")

    args = parser.parse_args()

    if args.command == "list":
        cmd_list(args)
        return

    path = resolve_log_path(args)
    events = parse_log(path)

    if args.command == "summary":
        cmd_summary(args, events, path)
    elif args.command == "startup":
        cmd_startup(args, events, path)
    elif args.command == "resync":
        cmd_resync(args, events, path)
    elif args.command == "events":
        cmd_events(args, events, path)


if __name__ == "__main__":
    main()
