#!/usr/bin/env python3
"""Independent check of trays/Sockets_*_ALL.json against the add-in's validation rules and the layout brief."""
import csv, glob, json, os, sys
sys.path.insert(0, os.path.dirname(__file__))
from make_socket_tray import text_w, bin_size, IN

PITCH, FIT, MIN_WEB, MIN_FLOOR = 42.0, 0.5, 0.8, 4.75 + 1.0   # the feet are 4.75 mm tall; keep 1 mm of floor above them
path = glob.glob(os.path.join("trays", "Sockets_*_ALL.json"))[0]
d = json.load(open(path))
errs = []
W, D = bin_size(d["UnitsX"]), bin_size(d["UnitsY"])
hw, hd = W / 2 - MIN_WEB, D / 2 - MIN_WEB
body, clr = 7.0 * d["HeightUnits"], d["DefaultClearance"]

if W > 51.5 * IN or D > 19.75 * IN: errs.append("tray exceeds 51.5 x 19.75 in")

boxes, tops = [], []
for p in d["Pockets"]:
    w, h = p["Width"] + clr, p["Height"] + clr
    b = (p["X"] - w / 2, p["X"] + w / 2, p["Y"] - h / 2, p["Y"] + h / 2, p["Name"])
    boxes.append(b)
    if b[0] < -hw or b[1] > hw or b[2] < -hd or b[3] > hd: errs.append("outside usable area: " + p["Name"])
    if p["Shape"] != "Cradle": errs.append("not a cradle: " + p["Name"])
    if body - p["Depth"] < MIN_FLOOR - 1e-6: errs.append("cradle bottom at %.2f mm is inside the feet: %s" % (body - p["Depth"], p["Name"]))
    if p["Depth"] < (p["Width"] + clr) / 2 + 0.5 - 0.05: errs.append("cradle too shallow to hold the socket: " + p["Name"])
    tops.append(body - p["Depth"] + p["Width"])      # socket lies on its side: top = floor + diameter
for i in range(len(boxes)):
    for j in range(i + 1, len(boxes)):
        a, b = boxes[i], boxes[j]
        if max(max(b[0] - a[1], a[0] - b[1]), max(b[2] - a[3], a[2] - b[3])) < MIN_WEB:
            errs.append("pockets too close: %s / %s" % (a[4], b[4]))

lboxes = []
for l in d["Labels"]:
    w = text_w(l["Text"], l["HeightMm"])
    lb = (l["X"], l["X"] + w, l["Y"] - l["HeightMm"] / 2, l["Y"] + l["HeightMm"] / 2, l["Text"])
    lboxes.append(lb)
    if lb[0] < -hw or lb[1] > hw or lb[2] < -hd or lb[3] > hd: errs.append("label outside area: " + l["Text"])
    for b in boxes:
        if lb[0] < b[1] and lb[1] > b[0] and lb[2] < b[3] and lb[3] > b[2]: errs.append("label '%s' overlaps %s" % (l["Text"], b[4]))
for i in range(len(lboxes)):
    for j in range(i + 1, len(lboxes)):
        a, b = lboxes[i], lboxes[j]
        if a[0] < b[1] and a[1] > b[0] and a[2] < b[3] and a[3] > b[2]: errs.append("labels overlap: %s / %s" % (a[4], b[4]))

# Brief: SAE left, Metric right; deep wells lowest; sizes below sockets; deep row centred in its half
sae = [b for b, p in zip(boxes, d["Pockets"]) if p["Name"].startswith("SAE")]
met = [b for b, p in zip(boxes, d["Pockets"]) if p["Name"].startswith("Metric")]
if max(b[1] for b in sae) > min(b[0] for b in met): errs.append("SAE and Metric halves overlap in X")
deep = [b for b, p in zip(boxes, d["Pockets"]) if p["Name"].endswith("deep")]
rest = [b for b, p in zip(boxes, d["Pockets"]) if not p["Name"].endswith("deep")]
if max(b[3] for b in deep) > min(b[2] for b in rest): errs.append("deep wells are not below the other rows")
# Each half is justified to the left: the first socket of every row/section starts at (about) the same X.
for side in ("SAE", "Metric"):
    lefts = {}
    for b, p in zip(boxes, d["Pockets"]):
        if p["Name"].startswith(side):
            key = (p["Name"].endswith("deep"), "12pt" in p["Name"], round(b[2] / 5))   # rough row identity
            lefts[key] = min(lefts.get(key, 1e9), b[0])
    spread_x = max(lefts.values()) - min(lefts.values())
    print("%-7s row start positions differ by %.1f mm across %d rows" % (side, spread_x, len(lefts)))
    if spread_x > 8: errs.append(side + " rows are not left-justified")

spread = max(tops) - min(tops)
if spread > 0.15: errs.append("socket tops differ by %.2f mm" % spread)
n_csv = len(list(csv.DictReader(open(os.path.join("data", "sockets_half_inch_drive.csv")))))
if n_csv != len(d["Pockets"]): errs.append("pockets %d != CSV rows %d" % (len(d["Pockets"]), n_csv))
for p in d["Pockets"]:
    if p["Depth"] < 8.0 - 1e-6: errs.append("grip too shallow: " + p["Name"])

print("%s: %d x %d cells (%.1f x %.1f mm = %.2f x %.2f in), %d mm tall, %d pockets, %d labels, tops %.1f-%.1f mm"
      % (os.path.basename(path), d["UnitsX"], d["UnitsY"], W, D, W / IN, D / IN, body, len(d["Pockets"]), len(d["Labels"]), min(tops), max(tops)))
for e in errs: print("  -", e)
print("ALL CHECKS PASSED" if not errs else "%d PROBLEM(S)" % len(errs))
sys.exit(1 if errs else 0)
