# -------------------------------------------------------
# File:    make_labelled_set.py
# Purpose: Sinh bộ ảnh thử CÓ NHÃN cho lệnh T3 (Chương 24, mục 24.4.1). Nhãn biết trước theo cách
#          dựng ảnh, không theo phần mềm: quy cách mẫu "đốm tối >= 100 px là lỗi; quá 50 lỗi là NG";
#          bụi (đốm nhỏ ~30-60 px) không phải lỗi.
# Dùng:    python make_labelled_set.py <thư mục samples\T3>
# -------------------------------------------------------
import os
import random
import sys

from PIL import Image, ImageDraw

WIDTH, HEIGHT = 640, 480
BACKGROUND, SPOT = 210, 40
DEFECT_RADIUS = 9      # ~250 px: lỗi thật
DUST_RADIUS = 3        # ~30-40 px: bụi, không phải lỗi
MAX_DEFECTS = 50       # quá số này là NG

# (tên, số lỗi thật, số hạt bụi). Nhãn = NG nếu số lỗi > MAX_DEFECTS.
CASES = [
    ("clean", 0, 0), ("few", 12, 0), ("few-dusty", 12, 120), ("edge-ok", 48, 40),
    ("dusty", 5, 200),
    ("many", 70, 0), ("many-dusty", 70, 150), ("edge-ng", 53, 20), ("lots", 110, 10),
    ("lots-dusty", 90, 220),
]


def place(rng, taken, radius):
    """Tâm đốm không chồng đốm khác — đếm được đúng số đốm đã vẽ."""
    for _ in range(10000):
        x = rng.randint(radius + 2, WIDTH - radius - 3)
        y = rng.randint(radius + 2, HEIGHT - radius - 3)
        if all((x - a) ** 2 + (y - b) ** 2 > (radius + r + 3) ** 2 for a, b, r in taken):
            taken.append((x, y, radius))
            return x, y
    raise RuntimeError("image too crowded")


def make(folder, name, defects, dust, seed):
    rng = random.Random(seed)
    image = Image.new("L", (WIDTH, HEIGHT), BACKGROUND)
    draw = ImageDraw.Draw(image)
    taken = []
    for radius, count in ((DEFECT_RADIUS, defects), (DUST_RADIUS, dust)):
        for _ in range(count):
            x, y = place(rng, taken, radius)
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=SPOT)
    label = "NG" if defects > MAX_DEFECTS else "OK"
    os.makedirs(os.path.join(folder, label), exist_ok=True)
    image.save(os.path.join(folder, label, f"{name}_{defects}d_{dust}s.bmp"))
    return label


def main():
    folder = sys.argv[1] if len(sys.argv) > 1 else "."
    for i, (name, defects, dust) in enumerate(CASES):
        print(make(folder, name, defects, dust, seed=1000 + i), name, defects, dust)


if __name__ == "__main__":
    main()
