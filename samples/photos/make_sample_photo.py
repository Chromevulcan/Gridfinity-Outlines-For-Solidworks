"""Makes drivers.jpg: three screwdrivers on a Letter sheet, photographed at an angle on a desk.
Known sizes, so the tracer can be checked: handles 24/28/32 mm across, shafts 6/7/8 mm, 210/235/250 mm long."""
import cv2, numpy as np
ppm = 6.0
W, H = 215.9, 279.4
sw, sh = int(round(W * ppm)), int(round(H * ppm))
sheet = np.full((sh, sw, 3), (238, 240, 242), np.uint8)

def P(x, y):  # mm, y up -> px
    return int(round(x * ppm)), int(round(sh - y * ppm))

def driver(x0, y0, handle_w, handle_l, shaft_w, shaft_l, colour):
    # Handle: a rounded bar; shaft: a thin bar with a tapered tip. Running along +y.
    cv2.rectangle(sheet, P(x0 - handle_w / 2, y0 + handle_l - handle_w / 2), P(x0 + handle_w / 2, y0 + handle_w / 2), colour, -1)
    cv2.circle(sheet, P(x0, y0 + handle_w / 2), int(handle_w / 2 * ppm), colour, -1)
    cv2.circle(sheet, P(x0, y0 + handle_l - handle_w / 2), int(handle_w / 2 * ppm), colour, -1)
    steel = (150, 150, 155)
    y1 = y0 + handle_l
    cv2.rectangle(sheet, P(x0 - shaft_w / 2, y1 + shaft_l - 6), P(x0 + shaft_w / 2, y1), (90, 90, 95), -1)
    tip = np.array([P(x0 - shaft_w / 2, y1 + shaft_l - 6), P(x0 + shaft_w / 2, y1 + shaft_l - 6), P(x0 + 1, y1 + shaft_l), P(x0 - 1, y1 + shaft_l)], np.int32)
    cv2.fillPoly(sheet, [tip], (90, 90, 95))

driver(45, 15, 24, 90, 6, 120, (40, 40, 200))     # red handle
driver(108, 15, 28, 100, 7, 135, (40, 150, 40))   # green
driver(170, 15, 32, 105, 8, 145, (180, 80, 30))   # blue

Wp, Hp = 2600, 2200
photo = np.zeros((Hp, Wp, 3), np.uint8)
yy, xx = np.mgrid[0:Hp, 0:Wp]
photo[:] = (40, 65, 90)
photo[..., 0] = (40 + 10 * np.sin(xx / 37.0)).astype(np.uint8)  # a little desk grain
src = np.float32([[0, 0], [sw, 0], [sw, sh], [0, sh]])
dst = np.float32([[700, 140], [1960, 230], [2050, 2010], [580, 1950]])
M = cv2.getPerspectiveTransform(src, dst)
warped = cv2.warpPerspective(sheet, M, (Wp, Hp), flags=cv2.INTER_LINEAR)
mask = cv2.warpPerspective(np.full((sh, sw), 255, np.uint8), M, (Wp, Hp), flags=cv2.INTER_NEAREST)
photo[mask > 0] = warped[mask > 0]
light = (1.0 - 0.22 * xx / Wp - 0.08 * yy / Hp)[..., None]
noisy = photo.astype(np.float32) * light + np.random.default_rng(1).normal(0, 3.5, photo.shape)
cv2.imwrite("drivers.jpg", np.clip(noisy, 0, 255).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 92])
print("wrote drivers.jpg")
