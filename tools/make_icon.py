"""Generate the Zh2En app icon (blue rounded square + white "L").

Run from the repository root:
    python tools/make_icon.py

Requires Pillow.  Produces:
    src/Zh2En.App/assets/app.png   (256x256 preview)
    src/Zh2En.App/assets/app.ico   (16/24/32/48/64/128/256)
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "Zh2En.App" / "assets"

SCALE = 1024
TOP = (0x3B, 0x7B, 0xF0)
BOTTOM = (0x1B, 0x3F, 0x8F)


def gradient(size: int) -> Image.Image:
    img = Image.new("RGB", (size, size))
    px = img.load()
    for y in range(size):
        for x in range(size):
            t = (x + y) / (2 * (size - 1))
            px[x, y] = tuple(round(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3))
    return img


def rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def make_master() -> Image.Image:
    base = gradient(SCALE).convert("RGBA")
    base.putalpha(rounded_mask(SCALE, radius=220))

    font = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 820)
    layer = Image.new("RGBA", (SCALE, SCALE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    box = draw.textbbox((0, 0), "L", font=font)
    w, h = box[2] - box[0], box[3] - box[1]
    draw.text(((SCALE - w) / 2 - box[0], (SCALE - h) / 2 - box[1] - 24), "L", font=font, fill="white")
    return Image.alpha_composite(base, layer)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    master = make_master()
    master.resize((256, 256), Image.LANCZOS).save(ASSETS / "app.png")
    master.save(ASSETS / "app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print(f"wrote {ASSETS / 'app.png'} and {ASSETS / 'app.ico'}")


if __name__ == "__main__":
    main()
