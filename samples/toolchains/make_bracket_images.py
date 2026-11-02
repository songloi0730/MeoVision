# -------------------------------------------------------
# File:    make_bracket_images.py
# Purpose: Sinh bộ ảnh mẫu CÓ ĐÁP ÁN cho các job nối nhiều tool (docs/vision-tools).
#          Tấm kim loại sáng trên nền tối: 2 lỗ tròn, 1 rãnh khuyết ở cạnh dưới (phá đối
#          xứng 180° cho PMAlign), xoay/dịch khác nhau từng ảnh. Đáp án biết trước theo cách
#          dựng ảnh, ghi vào truth.csv.
#          Thêm --overlay để vẽ hình minh hoạ vùng của từng tool (docs/vision-tools/img).
# Dùng:    python make_bracket_images.py <thư mục images> [--overlay <thư mục img>]
# -------------------------------------------------------
import csv
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

WIDTH, HEIGHT = 640, 480
SS = 4      # vẽ ở độ phân giải gấp 4 rồi thu nhỏ → cạnh có subpixel
BACKGROUND, PLATE, HOLE, DEFECT, DUST = 30, 200, 32, 70, 60

PLATE_W, PLATE_H = 360, 180             # hệ toạ độ TẤM (= không gian Fixture): gốc ở tâm tấm
HOLE_X, HOLE_R = 115, 24                # hai lỗ ở (±115; 0), bán kính 24 → bước lỗ 230
NOTCH_X0, NOTCH_X1, NOTCH_DEPTH = 60, 90, 14
DEFECT_ZONE_X, DEFECT_ZONE_Y = 70, 56   # lỗi: |x| <= 70, |y| <= 56 (vùng Blob ±80 × ±68)

# (tên, nhãn, tâm x, tâm y, góc độ, dài tấm, số lỗi, số bụi, thiếu lỗ phải)
CASES = [
    ("ok_01", "OK", 320, 240, 0.0, 360, 0, 0, False),        # ảnh train PMAlign — tư thế gốc
    ("ok_02", "OK", 350, 225, 8.5, 360, 0, 15, False),
    ("ok_03", "OK", 290, 260, -12.0, 360, 0, 25, False),
    ("ok_04", "OK", 335, 255, 14.0, 360, 0, 0, False),
    ("ng_defect_05", "NG", 305, 235, -5.0, 360, 3, 0, False),
    ("ng_defect_06", "NG", 330, 245, 11.0, 360, 1, 20, False),
    ("ng_hole_07", "NG", 315, 230, 3.0, 360, 0, 0, True),
    ("ng_long_08", "NG", 320, 240, -7.0, 380, 0, 10, False),
]


def to_image(cx, cy, angle_deg):
    """Tấm → ảnh; y hướng xuống, góc dương quay từ +x về +y (quy ước VisionPro)."""
    a = math.radians(angle_deg)
    c, s = math.cos(a), math.sin(a)
    return lambda x, y: (cx + c * x - s * y, cy + s * x + c * y)


def outline(length):
    """Đa giác viền tấm (có rãnh khuyết cạnh dưới), hệ toạ độ tấm."""
    w2, h2 = length / 2, PLATE_H / 2
    return [(-w2, -h2), (w2, -h2), (w2, h2), (NOTCH_X1, h2), (NOTCH_X1, h2 - NOTCH_DEPTH),
            (NOTCH_X0, h2 - NOTCH_DEPTH), (NOTCH_X0, h2), (-w2, h2)]


def scatter(rng, count, radius, taken):
    """Đốm trong vùng lỗi, không chồng nhau, không chồng đốm đã có."""
    spots = []
    for _ in range(count):
        for _ in range(10000):
            x = rng.uniform(-DEFECT_ZONE_X, DEFECT_ZONE_X)
            y = rng.uniform(-DEFECT_ZONE_Y, DEFECT_ZONE_Y)
            if all((x - a) ** 2 + (y - b) ** 2 > (radius + r + 4) ** 2 for a, b, r in taken):
                taken.append((x, y, radius))
                spots.append((x, y, radius))
                break
        else:
            raise RuntimeError("zone too crowded")
    return spots


def render(case, seed):
    name, label, cx, cy, angle, length, defects, dust, no_hole = case
    rng = random.Random(seed)
    m = to_image(cx, cy, angle)
    img = Image.new("L", (WIDTH * SS, HEIGHT * SS), BACKGROUND)
    d = ImageDraw.Draw(img)

    def circle(x, y, r, value):
        px, py = m(x, y)
        d.ellipse([(px - r) * SS, (py - r) * SS, (px + r) * SS, (py + r) * SS], fill=value)

    d.polygon([(m(x, y)[0] * SS, m(x, y)[1] * SS) for x, y in outline(length)], fill=PLATE)
    holes = [(-HOLE_X, 0)] + ([] if no_hole else [(HOLE_X, 0)])
    for hx, hy in holes:
        circle(hx, hy, HOLE_R, HOLE)
    taken = []
    for x, y, r in scatter(rng, defects, rng.choice([5, 6, 7]), taken):
        circle(x, y, r, DEFECT)
    for x, y, r in scatter(rng, dust, 1.5, taken):
        circle(x, y, r, DUST)

    img = img.resize((WIDTH, HEIGHT), Image.BOX).filter(ImageFilter.GaussianBlur(0.6))
    # Ánh sáng không đều (sáng hơn ở trái) + nhiễu cảm biến — giống ảnh thật hơn ảnh vẽ phẳng.
    px = img.load()
    for y in range(HEIGHT):
        for x in range(WIDTH):
            v = px[x, y] * (1.04 - 0.08 * x / WIDTH) + rng.gauss(0, 3)
            px[x, y] = max(0, min(255, int(round(v))))

    corner = m(-length / 2, -PLATE_H / 2)
    truth = {
        "file": name + ".bmp", "label": label, "center_x": cx, "center_y": cy,
        "angle_deg": angle, "width": length, "height": PLATE_H,
        "pitch": "" if no_hole else 2 * HOLE_X, "hole_diameter": 2 * HOLE_R,
        "defects": defects, "corner_x": round(corner[0], 2), "corner_y": round(corner[1], 2),
    }
    return img, truth


# ---------- hình minh hoạ: vùng của từng tool vẽ trong không gian tấm ----------
# Khớp với samples/toolchains/MakeToolChainJobs.cs — đổi vùng ở đó thì đổi ở đây.
def tool_shapes():
    w2, h2 = PLATE_W / 2, PLATE_H / 2
    return [
        ("rect", "Width (Caliper)", (0, -80, 440, 8, 0), (230, 60, 60)),
        ("rect", "Height (Caliper)", (-160, 0, 240, 8, 90), (230, 60, 60)),
        ("circle", "HoleLeft (FindCircle)", (-HOLE_X, 0, HOLE_R), (40, 140, 255)),
        ("circle", "HoleRight (FindCircle)", (HOLE_X, 0, HOLE_R), (40, 140, 255)),
        ("rect", "Defects (Blob)", (0, 0, 160, 136, 0), (255, 190, 0)),
        ("line", "TopEdge (FindLine)", (-160, -h2, 160, -h2), (60, 200, 90)),
        ("line", "LeftEdge (FindLine)", (-w2, 70, -w2, -70), (60, 200, 90)),
    ]


def draw_overlay(base, cx, cy, angle, title):
    m = to_image(cx, cy, angle)
    k = 2                                           # phóng 2 lần cho chữ dễ đọc
    img = base.convert("RGB").resize((WIDTH * k, HEIGHT * k), Image.LANCZOS)
    d = ImageDraw.Draw(img)

    def p(x, y):
        a, b = m(x, y)
        return a * k, b * k

    def rect(cxr, cyr, lx, ly, rot_deg):
        r = math.radians(rot_deg)
        ux, uy = math.cos(r), math.sin(r)
        vx, vy = -uy, ux
        pts = []
        for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            pts.append(p(cxr + sx * lx / 2 * ux + sy * ly / 2 * vx,
                         cyr + sx * lx / 2 * uy + sy * ly / 2 * vy))
        return pts

    for kind, _, g, color in tool_shapes():
        if kind == "rect":
            pts = rect(*g)
            d.line(pts + [pts[0]], fill=color, width=3)
        elif kind == "circle":
            x, y, r = g
            steps = [t / 24 * 2 * math.pi for t in range(25)]
            pts = [p(x + r * math.cos(t), y + r * math.sin(t)) for t in steps]
            d.line(pts, fill=color, width=3)
        else:
            d.line([p(g[0], g[1]), p(g[2], g[3])], fill=color, width=4)
    # Trục Fixture: gốc ở tâm tấm, x đỏ, y xanh lá — thứ PMAlign tìm ra và Fixture gắn vào ảnh.
    o = p(0, 0)
    d.line([o, p(60, 0)], fill=(255, 0, 0), width=4)
    d.line([o, p(0, 60)], fill=(0, 255, 0), width=4)
    d.text((o[0] + 6, o[1] + 6), "Fixture (0;0)", fill=(255, 255, 255))
    d.rectangle([0, 0, WIDTH * k, 26], fill=(0, 0, 0))
    d.text((8, 7), title, fill=(255, 255, 255))
    y = HEIGHT * k - 18 * len(tool_shapes()) - 8
    for _, label, _, color in tool_shapes():
        d.rectangle([8, y + 3, 20, y + 15], fill=color)
        d.text((26, y + 2), label, fill=(255, 255, 255))
        y += 18
    return img


def main():
    if len(sys.argv) < 2:
        print(__doc__ or "python make_bracket_images.py <images> [--overlay <img>]")
        return 2
    folder = sys.argv[1]
    os.makedirs(folder, exist_ok=True)
    rows = []
    rendered = {}
    for i, case in enumerate(CASES):
        img, truth = render(case, seed=1000 + i)
        img.save(os.path.join(folder, truth["file"]))
        rendered[case[0]] = (img, case)
        rows.append(truth)
        print(truth["file"], truth["label"])
    with open(os.path.join(folder, "truth.csv"), "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)

    if "--overlay" in sys.argv:
        out = sys.argv[sys.argv.index("--overlay") + 1]
        os.makedirs(out, exist_ok=True)
        for key, title in (("ok_01", "ok_01.bmp - train pose (0 deg): regions in Fixture"),
                           ("ok_03", "ok_03.bmp - part rotated -12 deg: same regions follow")):
            img, case = rendered[key]
            draw_overlay(img, case[2], case[3], case[4], title).save(
                os.path.join(out, "regions_" + key + ".png"))
            print("overlay", key)
    return 0


if __name__ == "__main__":
    sys.exit(main())
