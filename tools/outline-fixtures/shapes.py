# Synthetic "traced" tools in outline-app's internal form: mm on an A4 sheet, Y down, jittered like a photo trace.
import json, math, random
from shapely.geometry import Polygon, Point, box
from shapely import affinity
from shapely.ops import unary_union
random.seed(7)

def trace(geom, step=0.35, jitter=0.12):
    def ring(coords):
        pts=[]; c=list(coords)
        for (x0,y0),(x1,y1) in zip(c,c[1:]):
            L=math.hypot(x1-x0,y1-y0); n=max(1,int(L/step))
            for k in range(n):
                t=k/n; pts.append([round(x0+(x1-x0)*t+random.uniform(-jitter,jitter),2), round(y0+(y1-y0)*t+random.uniform(-jitter,jitter),2)])
        return {"points":[{"x":p[0],"y":p[1]} for p in pts]}
    return {"outline":ring(geom.exterior.coords), "holes":[ring(i.coords) for i in geom.interiors]}

# 13 mm combination wrench, 170 mm: box end (ring with hole) and open end with jaws.
shaft = box(-62,-6,62,6)
box_end = Point(-72,0).buffer(13.5, 64)
open_head = Point(70,0).buffer(15, 64)
jaw = affinity.rotate(box(68,-6.6,100,6.6), 15, origin=(70,0))
wrench = unary_union([shaft, box_end, open_head]).difference(jaw).difference(Point(-72,0).buffer(8.2, 12))
wrench = affinity.rotate(wrench, 23, origin=(0,0))
wrench = affinity.translate(wrench, 105, 110)

# Needle-nose pliers (closed): concave handles meeting at a pivot.
handle1 = Polygon([(-5,-3),(-90,-14),(-95,-9),(-90,-5),(-8,3)])
handle2 = affinity.scale(handle1, 1, -1, origin=(0,0))
head = Polygon([(-8,-7),(8,-7),(55,-2),(58,0),(55,2),(8,7),(-8,7)])
pliers = unary_union([handle1.buffer(2.5), handle2.buffer(2.5), head, Point(0,0).buffer(9)])
pliers = affinity.translate(affinity.rotate(pliers, -8, origin=(0,0)), 105, 220)

# 5 mm hex key (L shape).
hexkey = unary_union([box(0,0,80,5), box(0,0,5,25)]).buffer(0.6)
hexkey = affinity.translate(affinity.rotate(hexkey, 40, origin=(0,0)), 40, 40)

out = {"paper":{"width":210,"height":297}, "contours":{"wrench-13mm":trace(wrench), "pliers":trace(pliers), "hex-key-5mm":trace(hexkey)}}
json.dump(out, open("contours.json","w"))
for k,v in out["contours"].items(): print(k, len(v["outline"]["points"]), "pts,", len(v["holes"]), "holes")
