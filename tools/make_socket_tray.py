#!/usr/bin/env python3
"""
Builds ONE large Gridfinity tray holding every socket in data/sockets_half_inch_drive.csv, laid out for use:

    SAE on the left half, Metric on the right half, everything in each half justified to the left.
    Sections top to bottom: 6-point, 12-point, then the 3.125" deep wells at the bottom.
    The 6-point sections wrap onto several rows (reading left to right, small to large); the generator tries
    1 to 4 rows and keeps the one that gives the smallest tray inside the size limit.
    A title centred above every section; each socket's size centred below it.
    Sockets lie flat with their long axis pointing front to back (up the Y axis), tops all at one level,
    each in a round-bottomed cradle sized to its diameter plus the clearance.

Output: trays/Sockets_1-2in_ALL.json  (+ trays/SUMMARY.md)
Usage : python tools/make_socket_tray.py [--max-width-in 51.5] [--max-depth-in 19.75] [--clearance 0.6]
                                         [--center-deep] [--wrap 2,1,1] [--optimize-all]
"""
import argparse
import csv
import itertools
import json
import math
import os
from collections import OrderedDict

IN = 25.4
PITCH = 42.0
FIT = 0.5

# Layout, all in mm
EDGE = 5.0              # free space between everything and the tray edge
CELL_GAP = 3.0          # between neighbouring sockets in a row
LABEL_PAD = 3.0         # extra width a size label needs beyond its text
CENTER_GAP = 14.0       # between the SAE half and the Metric half
SECTION_GAP = 9.0       # between one section's last size labels and the next section's title
ROW_GAP = 6.0           # between a row's size labels and the next wrapped row's sockets
TITLE_H = 7.0           # character height of section titles
TEXT_H = 6.0            # character height of size labels
TITLE_GAP = 4.0         # between a title and the sockets under it
LABEL_GAP = 3.0         # between the sockets and their size labels
CORNER = 2.0
FLOOR = 6.0             # lowest point of the deepest cradle above the tray base: the 4.75 mm feet + 1.25 mm of floor
MIN_DEPTH = 8.0
HOLD = 0.5              # every socket's centre line sits at least this far below the tray top (cradle radius + HOLD deep)
LABEL_DEPTH = 0.8       # four layers at 0.2 mm

# Arial Bold advance widths (fraction of the font size) for centring text; anything else gets 0.6.
ADV = {' ': .278, '/': .278, '"': .474, '-': .333, '.': .278,
       'A': .722, 'B': .722, 'C': .722, 'D': .722, 'E': .667, 'F': .611, 'G': .778, 'H': .722, 'I': .278,
       'J': .556, 'K': .722, 'L': .611, 'M': .833, 'N': .722, 'O': .778, 'P': .667, 'Q': .778, 'R': .722,
       'S': .667, 'T': .611, 'U': .722, 'V': .667, 'W': .944, 'X': .667, 'Y': .667, 'Z': .611}
ADV.update({d: .556 for d in "0123456789"})

SIDES = ["SAE", "Metric"]                                        # left, right
SECTIONS = [("Standard", 6), ("Standard", 12), ("Deep", 6)]      # top to bottom; deep wells at the bottom


def text_w(text, h):
    return sum(ADV.get(c, 0.6) for c in text) * h


def bin_size(units):
    return units * PITCH - FIT


def units_for(extent):
    """Smallest number of 42 mm cells whose bin is at least `extent` long."""
    return max(1, int(math.ceil((extent + FIT) / PITCH - 1e-9)))


class Sock:
    def __init__(self, row, clearance):
        self.system, self.style, self.points = row["system"], row["style"], int(row["points"])
        self.size = row["size"]
        self.length = float(row["length_in"]) * IN
        self.dia = float(row["diameter_in"]) * IN
        self.lc = self.length + clearance
        self.dc = self.dia + clearance
        self.cell_w = max(self.dc, text_w(self.size, TEXT_H) + LABEL_PAD)


def row_width(socks):
    return sum(s.cell_w for s in socks) + CELL_GAP * (len(socks) - 1)


def partition(socks, k):
    """Split into k consecutive rows with the most even widths (smallest widest row, then least spread)."""
    k = min(k, len(socks))
    if k <= 1:
        return [socks]
    best, best_key = None, None
    for cuts in itertools.combinations(range(1, len(socks)), k - 1):
        edges = (0,) + cuts + (len(socks),)
        rows = [socks[a:b] for a, b in zip(edges, edges[1:])]
        widths = [row_width(r) for r in rows]
        key = (round(max(widths), 3), round(max(widths) - min(widths), 3))
        if best_key is None or key < best_key:
            best, best_key = rows, key
    return best


def level_depths(socks):
    """Tray height (7 mm units) and pocket depths that put every socket top at the same height T:  d = B - T + D."""
    t = max(s.dia for s in socks) + FLOOR
    units = 1
    while True:
        body = 7.0 * units
        depths = [round(body - t + s.dia, 1) for s in socks]
        if all(d >= max(MIN_DEPTH, s.dc / 2 + HOLD) for d, s in zip(depths, socks)):
            return units, t, depths
        units += 1


def section_title(system, style, points, drive):
    kind = "DEEP %d POINT" % points if style == "Deep" else "%d POINT - STANDARD" % points
    return '%s" DRIVE - %s - %s' % (drive, system.upper(), kind)


def plan(socks, ks, drive, center_deep):
    """Geometry for one choice of row counts ks = (rows for 6pt, rows for 12pt, rows for deep)."""
    # rows[side][sec] = list of rows, each a list of sockets sorted small to large (stable on CSV order)
    rows = {}
    for side in SIDES:
        rows[side] = []
        for (style, points), k in zip(SECTIONS, ks):
            members = sorted([s for s in socks if (s.system, s.style, s.points) == (side, style, points)],
                             key=lambda s: s.dia)
            rows[side].append(partition(members, k) if members else [])

    # Row heights are shared by both halves so each row lines up across the tray.
    n_rows = [max(len(rows[side][i]) for side in SIDES) for i in range(len(SECTIONS))]
    row_len = [[max((max(s.lc for s in rows[side][i][r]) for side in SIDES
                     if r < len(rows[side][i]) and rows[side][i][r]), default=0.0)
                for r in range(n_rows[i])] for i in range(len(SECTIONS))]
    sec_h = [TITLE_H + TITLE_GAP + sum(L + LABEL_GAP + TEXT_H for L in row_len[i]) + ROW_GAP * (n_rows[i] - 1)
             if n_rows[i] else 0.0 for i in range(len(SECTIONS))]
    active = [i for i in range(len(SECTIONS)) if n_rows[i]]

    # Width of each section block = widest row or its title; a half is as wide as its widest section.
    block_w, titles = {}, {}
    for side in SIDES:
        for i in active:
            if not rows[side][i]:
                continue
            style, points = SECTIONS[i]
            titles[(side, i)] = section_title(side, style, points, drive)
            block_w[(side, i)] = max(max(row_width(r) for r in rows[side][i]), text_w(titles[(side, i)], TITLE_H))
    side_w = {side: max(block_w[(side, i)] for i in active if (side, i) in block_w) for side in SIDES}

    content_w = side_w["SAE"] + CENTER_GAP + side_w["Metric"]
    content_h = sum(sec_h[i] for i in active) + SECTION_GAP * (len(active) - 1)
    ux, uy = units_for(content_w + 2 * EDGE), units_for(content_h + 2 * EDGE)

    return dict(ks=ks, rows=rows, row_len=row_len, sec_h=sec_h, active=active, block_w=block_w, titles=titles,
                side_w=side_w, content_w=content_w, content_h=content_h, ux=ux, uy=uy,
                area=bin_size(ux) * bin_size(uy), n_rows_total=sum(n_rows), center_deep=center_deep)


def place(p, drive):
    """Turn a plan into pocket centres and label positions (X right, Y up, origin at the tray centre)."""
    left, top = -p["content_w"] / 2, p["content_h"] / 2
    side_x0 = {"SAE": left, "Metric": left + p["side_w"]["SAE"] + CENTER_GAP}
    ordered, xy, labels = [], [], []
    y = top
    for i in p["active"]:
        y_title = y
        y = y - TITLE_H - TITLE_GAP
        for side in SIDES:
            if not p["rows"][side][i]:
                continue
            bw = p["block_w"][(side, i)]
            shift = (p["side_w"][side] - bw) / 2 if (p["center_deep"] and SECTIONS[i][0] == "Deep") else 0.0
            x0 = side_x0[side] + shift
            title = p["titles"][(side, i)]
            labels.append(dict(text=title, h=TITLE_H, x=x0 + bw / 2 - text_w(title, TITLE_H) / 2, y=y_title - TITLE_H / 2))

        y_row = y
        for r, L in enumerate(p["row_len"][i]):
            baseline = y_row - L                                  # pocket bottoms in this row line up here
            for side in SIDES:
                if r >= len(p["rows"][side][i]):
                    continue
                bw = p["block_w"][(side, i)]
                shift = (p["side_w"][side] - bw) / 2 if (p["center_deep"] and SECTIONS[i][0] == "Deep") else 0.0
                x = side_x0[side] + shift
                for s in p["rows"][side][i][r]:
                    cx = x + s.cell_w / 2
                    ordered.append(s)
                    xy.append((cx, baseline + s.lc / 2))
                    labels.append(dict(text=s.size, h=TEXT_H, x=cx - text_w(s.size, TEXT_H) / 2,
                                       y=baseline - LABEL_GAP - TEXT_H / 2))
                    x += s.cell_w + CELL_GAP
            y_row = baseline - LABEL_GAP - TEXT_H - ROW_GAP
        y = y_title - p["sec_h"][i] - SECTION_GAP
    return ordered, xy, labels


def main():
    global FLOOR
    ap = argparse.ArgumentParser()
    ap.add_argument("--csv", default=os.path.join("data", "sockets_half_inch_drive.csv"))
    ap.add_argument("--out", default="trays")
    ap.add_argument("--drive", default="1/2")
    ap.add_argument("--max-width-in", type=float, default=51.5)
    ap.add_argument("--max-depth-in", type=float, default=19.75)
    ap.add_argument("--clearance", type=float, default=0.4, help="total clearance on diameter and length, mm")
    ap.add_argument("--floor", type=float, default=FLOOR, help="height of the lowest cradle point above the tray base, mm (at least 5.75)")
    ap.add_argument("--center-deep", action="store_true", help="centre the deep-well rows in their half instead of left-justifying")
    ap.add_argument("--wrap", default=None, help="force the number of rows per section, e.g. 2,2,1")
    ap.add_argument("--optimize-all", action="store_true", help="also try wrapping the 12-point and deep-well rows")
    args = ap.parse_args()

    FLOOR = args.floor
    socks = [Sock(r, args.clearance) for r in csv.DictReader(open(args.csv))]
    max_w, max_d = args.max_width_in * IN, args.max_depth_in * IN

    if args.wrap:
        candidates = [tuple(int(v) for v in args.wrap.split(","))]
    elif args.optimize_all:
        candidates = list(itertools.product(range(1, 5), range(1, 4), range(1, 3)))
    else:
        candidates = [(k, 1, 1) for k in range(1, 5)]          # wrap only the 6-point sockets
    plans = [plan(socks, ks, args.drive, args.center_deep) for ks in candidates]
    fits = [p for p in plans if bin_size(p["ux"]) <= max_w and bin_size(p["uy"]) <= max_d]
    if not fits:
        raise SystemExit("No arrangement fits inside %.1f x %.1f in" % (args.max_width_in, args.max_depth_in))
    # Smallest tray first; then fewest wrapped rows (simplest), then narrowest.
    fits.sort(key=lambda p: (round(p["area"]), p["n_rows_total"], p["content_w"]))
    best = fits[0]
    print("Row counts tried (6pt, 12pt, deep) -> cells; best five:")
    for p in fits[:5]:
        print("  %s -> %d x %d cells (%.1f x %.1f in)" % (p["ks"], p["ux"], p["uy"], bin_size(p["ux"]) / IN, bin_size(p["uy"]) / IN))

    ordered, xy, labels = place(best, args.drive)
    ux, uy = best["ux"], best["uy"]
    W, H = bin_size(ux), bin_size(uy)
    height_units, top_z, depths = level_depths(ordered)

    pockets = []
    for s, (px, py), depth in zip(ordered, xy, depths):
        pockets.append(OrderedDict([
            ("Name", "%s %s%s %dpt" % (s.system, s.size, " mm" if s.system == "Metric" else " in", s.points) +
                     (" deep" if s.style == "Deep" else "")),
            ("Shape", "Cradle"),
            ("Width", round(s.dia, 2)),             # across the socket (X)
            ("Height", round(s.length, 2)),         # along the socket (Y, front to back)
            ("Depth", depth),
            ("X", round(px, 2)),
            ("Y", round(py, 2)),
            ("RotationDeg", 0),
            ("AllowRotate", False),
        ]))

    definition = OrderedDict([
        ("Name", "Sockets_%sin_ALL" % args.drive.replace("/", "-")),
        ("UnitsX", ux), ("UnitsY", uy), ("HeightUnits", height_units),
        ("Style", "SolidBlock"), ("MagnetHoles", False),
        ("AutoLayout", False), ("AutoGrowGrid", False),
        ("DefaultClearance", args.clearance),
        ("Pockets", pockets),
        ("Labels", [OrderedDict([("Text", l["text"]), ("X", round(l["x"], 2)), ("Y", round(l["y"], 2)),
                                 ("HeightMm", l["h"])]) for l in labels]),
        ("LabelDepth", LABEL_DEPTH), ("LabelFont", "Arial"), ("LabelBold", True),
    ])

    os.makedirs(args.out, exist_ok=True)
    for f in os.listdir(args.out):
        if f.endswith(".json") or f == "SUMMARY.md":
            os.remove(os.path.join(args.out, f))
    path = os.path.join(args.out, definition["Name"] + ".json")
    with open(path, "w") as fh:
        json.dump(definition, fh, indent=2)

    summary = [
        "# Socket tray (%s\" drive)\n" % args.drive,
        "- File: `%s`" % os.path.basename(path),
        "- Grid: %d x %d cells = %.1f x %.1f mm (%.2f x %.2f in); limit %.2f x %.2f in"
        % (ux, uy, W, H, W / IN, H / IN, args.max_width_in, args.max_depth_in),
        "- Rows per section (6-point, 12-point, deep): %s" % (best["ks"],),
        "- Height: %d mm; every socket top sits %.1f mm above the tray base" % (height_units * 7, top_z),
        "- Sockets: %d (SAE %d left, Metric %d right)" % (
            len(ordered), sum(1 for s in ordered if s.system == "SAE"), sum(1 for s in ordered if s.system == "Metric")),
        "- Pocket depths: %.1f to %.1f mm" % (min(depths), max(depths)),
    ]
    with open(os.path.join(args.out, "SUMMARY.md"), "w") as fh:
        fh.write("\n".join(summary) + "\n")
    print("\n".join(summary))


if __name__ == "__main__":
    main()
