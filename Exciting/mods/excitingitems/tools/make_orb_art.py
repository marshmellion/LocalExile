# Builds the custom orbs' pictures from the game's own currency art (.dds files, 80 x 80): recoloured, with glow, so
# they sit beside the real orbs. The game's art is not ours to share, so the pictures are made on each player's own
# computer from their own client (the setup does it; without Python and Pillow the orbs keep their simple art).
#   make_orb_art.py --client <LocalExile folder>       reads the client there, puts the pictures into the content packs
#   make_orb_art.py <folder with the .dds> <out folder> [preview.png]
import sys, math, os, glob, re, base64, subprocess, tempfile, json
try:
    from PIL import Image, ImageFilter, ImageChops, ImageDraw, ImageEnhance
except ImportError:
    print("make_orb_art: Python's Pillow is missing (python -m pip install pillow); the orbs keep their simple art")
    sys.exit(3)
import colorsys

GAME_ART = ["Art/2DItems/Currency/CurrencyModValues.dds", "Art/2DItems/Currency/EldritchChaosOrb.dds",
            "Art/2DItems/Currency/CurrencyRerollSocketNumbers.dds", "Art/2DItems/Currency/CurrencyVaal.dds"]
# Content pack picture (its path's file name) -> the picture this script makes.
PACK_PICTURES = {"RadiantOrb.dds": "radiant_orb.png", "KrangleOrb.dds": "krangle_orb.png",
                 "LimitBreakOrb.dds": "limit_break_orb.png", "DefiledVaalOrb.dds": "defiled_vaal_orb.png"}

def export_from_client(root, paths, to):
    """The files as the client has them (before any patch), through LocalExile's content patcher."""
    project = os.path.join(root, "tools", "ContentPatch")
    for i in range(0, len(paths), 60):
        r = subprocess.run(["dotnet", "run", "--project", project, "-c", "Release", "--", "export", to] + paths[i:i + 60],
                           cwd=root, capture_output=True, text=True)
        if r.returncode != 0:
            raise SystemExit("make_orb_art: the content patcher could not read the client:\n" + r.stdout[-2000:] + r.stderr[-2000:])

client_mode = len(sys.argv) > 2 and sys.argv[1] == "--client"
if client_mode:
    root = os.path.abspath(sys.argv[2])
    src = tempfile.mkdtemp(prefix="orbart")
    export_from_client(root, GAME_ART, src)
    out = tempfile.mkdtemp(prefix="orbout")
else:
    src, out = sys.argv[1], sys.argv[2]

def load(name):
    hits = [f for f in glob.glob(os.path.join(src, "**", "*.dds"), recursive=True) if os.path.basename(f).lower() == name.lower() + ".dds"]
    return Image.open(hits[0] if hits else f"{src}/{name}.dds").convert("RGBA")

def tint(im, shadow, mid, high, keep=0.25):
    """Gradient map by brightness: dark -> shadow, middle -> mid, light -> high, mixed with a little of the original."""
    px = im.load(); w, h = im.size
    res = Image.new("RGBA", im.size)
    rp = res.load()
    def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            l = (0.299 * r + 0.587 * g + 0.114 * b) / 255
            c = lerp(shadow, mid, l / 0.5) if l < 0.5 else lerp(mid, high, (l - 0.5) / 0.5)
            c = tuple(int(c[i] * (1 - keep) + (r, g, b)[i] * keep) for i in range(3))
            rp[x, y] = (*c, a)
    return res

def glow(im, colour, radius, strength):
    a = im.split()[3].filter(ImageFilter.GaussianBlur(radius))
    a = a.point(lambda v: min(255, int(v * strength)))
    g = Image.new("RGBA", im.size, (*colour, 0)); g.putalpha(a)
    return g

def rays(size, colour, count, inner, outer, width, blur):
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer); c = size / 2
    for i in range(count):
        ang = 2 * math.pi * i / count
        w = width * (1 if i % 2 == 0 else 0.6); o = outer * (1 if i % 2 == 0 else 0.8)
        pts = [(c + inner * math.cos(ang - w), c + inner * math.sin(ang - w)),
               (c + o * math.cos(ang), c + o * math.sin(ang)),
               (c + inner * math.cos(ang + w), c + inner * math.sin(ang + w))]
        d.polygon(pts, fill=(*colour, 200))
    return layer.filter(ImageFilter.GaussianBlur(blur))

def compose(base, layers_below, layers_above=()):
    canvas = Image.new("RGBA", base.size, (0, 0, 0, 0))
    for l in layers_below: canvas = Image.alpha_composite(canvas, l)
    canvas = Image.alpha_composite(canvas, base)
    for l in layers_above: canvas = Image.alpha_composite(canvas, l)
    return canvas

def shrink(im, scale):
    w, h = im.size; s = im.resize((int(w * scale), int(h * scale)), Image.LANCZOS)
    c = Image.new("RGBA", im.size, (0, 0, 0, 0)); c.paste(s, ((w - s.width) // 2, (h - s.height) // 2), s); return c

def highlight(im, colour, amount):
    """Screen a soft light from the upper left over the orb (the game's art is lit from there)."""
    w, h = im.size
    light = Image.new("L", im.size, 0); d = ImageDraw.Draw(light)
    d.ellipse((w * 0.12, h * 0.08, w * 0.62, h * 0.55), fill=int(255 * amount))
    light = light.filter(ImageFilter.GaussianBlur(w * 0.12))
    light = ImageChops.multiply(light, im.split()[3])
    layer = Image.new("RGBA", im.size, (*colour, 0)); layer.putalpha(light)
    return Image.alpha_composite(im, layer)

def premultiply(im):
    """The game's item art is premultiplied (colour already times alpha; the client draws colour + background x (1 - alpha)).
    Straight colour shows a soft glow as a solid disc, so every picture is written premultiplied."""
    r, g, b, a = im.split()
    return Image.merge("RGBA", [ImageChops.multiply(c, a) for c in (r, g, b)] + [a])

def as_game_draws(im, background):
    """A premultiplied picture over a background, as the client blends it."""
    bg = Image.new("RGBA", im.size, (*background, 255))
    inv = ImageChops.invert(im.split()[3])
    out = [ImageChops.add(c, ImageChops.multiply(bc, inv)) for c, bc in zip(im.split()[:3], bg.split()[:3])]
    return Image.merge("RGBA", out + [Image.new("L", im.size, 255)])

# Radiant Orb: the Divine Orb, sun-gold, rays behind it.
b = shrink(load("currencymodvalues"), 0.86)
b = tint(b, (40, 14, 0), (215, 125, 20), (255, 240, 185), keep=0.3)
b = ImageEnhance.Contrast(b).enhance(1.35)
b = highlight(b, (255, 250, 220), 0.3)
radiant = compose(b, [rays(80, (255, 185, 50), 16, 20, 40, 0.13, 1.0), glow(b, (255, 170, 60), 5, 1.3)])
radiant = premultiply(radiant)
radiant.save(f"{out}/radiant_orb.png")

# Krangle Orb: the Eldritch Chaos Orb in blood red, crimson mist around it.
b = shrink(load("eldritchchaosorb"), 0.9)
b = tint(b, (25, 0, 4), (170, 18, 28), (255, 170, 150), keep=0.12)
b = ImageEnhance.Contrast(b).enhance(1.2)
b = highlight(b, (255, 200, 190), 0.25)
krangle = compose(b, [glow(b, (150, 0, 20), 6, 1.5), glow(b, (255, 40, 40), 2.5, 0.6)])
krangle = premultiply(krangle)
krangle.save(f"{out}/krangle_orb.png")

# Limit Break Orb: the Jeweller's Orb, molten gold with a fiery edge.
b = shrink(load("currencyrerollsocketnumbers"), 0.92)
b = tint(b, (30, 6, 0), (200, 80, 10), (255, 225, 130), keep=0.3)
b = ImageEnhance.Contrast(b).enhance(1.5)
b = highlight(b, (255, 245, 200), 0.25)
limit = compose(b, [glow(b, (255, 80, 0), 5, 1.4)])
limit = premultiply(limit)
limit.save(f"{out}/limit_break_orb.png")

# Defiled Vaal Orb: the Vaal Orb, darkened to purple with a sickly green aura.
b = shrink(load("currencyvaal"), 0.9)
b = tint(b, (12, 0, 18), (95, 30, 120), (190, 255, 170), keep=0.15)
b = ImageEnhance.Contrast(b).enhance(1.2)
defiled = compose(b, [glow(b, (60, 200, 70), 5, 1.3), glow(b, (90, 20, 120), 2.5, 0.8)])
defiled = premultiply(defiled)
defiled.save(f"{out}/defiled_vaal_orb.png")

# As the game draws them, on the inventory's dark blue and on a light background.
sheet = Image.new("RGBA", (4 * 88, 176), (24, 21, 19, 255))
for i, im in enumerate([radiant, krangle, limit, defiled]):
    sheet.paste(as_game_draws(im, (14, 16, 38)), (i * 88 + 4, 4))
    sheet.paste(as_game_draws(im, (90, 85, 70)), (i * 88 + 4, 92))
if not client_mode:
    sheet.save(sys.argv[3] if len(sys.argv) > 3 else f"{out}/orb_art_preview.png")
else:
    # Into the content packs (imageBase64 of each orb's .dds entry); the next start puts them into the game.
    content = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "content")
    changed = 0
    for pack in glob.glob(os.path.join(content, "*.json")):
        text = open(pack, encoding="utf-8").read()
        def swap(m):
            global changed
            name = m.group(1).split("/")[-1]
            if name not in PACK_PICTURES: return m.group(0)
            changed += 1
            data = base64.b64encode(open(os.path.join(out, PACK_PICTURES[name]), "rb").read()).decode()
            return m.group(0).replace(m.group(2), data)
        new = re.sub(r'"path":\s*"([^"]+)",\s*"imageBase64":\s*"([^"]+)"', swap, text)
        if new != text: open(pack, "w", encoding="utf-8", newline="").write(new)
    print(f"make_orb_art: {changed} orb picture(s) made from your client's art")
