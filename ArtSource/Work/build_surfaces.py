"""Builds the surface texture sets used by the procedural floors (walls, floors, ceilings, trim, props).

Art direction: a maid cafe that used to be cute and playful and has been rotting for years. Pastel colours are
kept but faded, stained, peeling and grimy.

Inputs: Poly Haven (CC0) 2K maps in <src>/<asset_id>/: <id>_diff_2k.jpg, <id>_nor_gl_2k.jpg, <id>_arm_2k.jpg.
Outputs, per surface, in <out>/<Name>/:
  <Name>_Albedo.jpg  2048 px. Ambient occlusion is partly baked in (the game's ambient light is almost black,
                     so URP's occlusion map alone would have no visible effect).
  <Name>_Normal.jpg  2048 px, OpenGL convention (green = up), as Unity expects.
  <Name>_Mask.png    1024 px, URP Lit metallic/smoothness map: R metallic, G occlusion, B unused, A smoothness.
plus <out>/surfaces.json (real-world tile size per set), read by Scripts/Editor/SurfaceAssetPipeline.cs.

The three wallpapers are original patterns drawn here (stripes, hearts, dots, stars, lace border, lower panel),
aged and composited over Poly Haven's worn_plaster_wall, which shows through where the paper has peeled off.
One wallpaper tile covers 3 m x 3 m, i.e. exactly one wall height (floors start at y = 0, walls are 3 m tall),
so the lower panel, the border, ceiling drips and floor grime always sit at the right height.

Usage (from the project root):
  python ArtSource/Work/build_surfaces.py ArtSource/Downloads/PolyHaven Assets/_Project/Textures/Surfaces [Name,...]
"""
import json
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw

SIZE = 2048       # maximum texture size (2K): albedo and normal maps
MASK_SIZE = 1024  # metallic/smoothness maps carry low-frequency data, half resolution is enough


# ------------------------------------------------------------------ helpers

def C(r, g, b):
    return np.array((r, g, b), np.float32) / 255.0


def load(path):
    return np.asarray(Image.open(path).convert('RGB'), dtype=np.float32) / 255.0


def to_img(a, mode='RGB'):
    return Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), mode)


def resize(a, w, h):
    return np.asarray(to_img(a).resize((w, h), Image.LANCZOS), dtype=np.float32) / 255.0


def lum(a):
    return a[..., 0] * 0.2126 + a[..., 1] * 0.7152 + a[..., 2] * 0.0722


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def blur(a, sigma):
    """Gaussian blur through the FFT: wraps around the edges, so tileable input stays tileable."""
    if a.ndim == 3:
        return np.stack([blur(a[..., c], sigma) for c in range(a.shape[2])], -1)
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.rfftfreq(w)[None, :]
    g = np.exp(-2.0 * (np.pi * sigma) ** 2 * (fx ** 2 + fy ** 2))
    return np.fft.irfft2(np.fft.rfft2(a) * g, s=(h, w)).astype(np.float32)


def blur1d(a, sigma):
    f = np.fft.rfftfreq(a.shape[0])
    return np.fft.irfft(np.fft.rfft(a) * np.exp(-2.0 * (np.pi * sigma) ** 2 * f ** 2), n=a.shape[0])


def gauss_noise(h, w, sigma, seed):
    """Tileable smooth noise, zero mean, unit variance."""
    n = blur(np.random.default_rng(seed).standard_normal((h, w)).astype(np.float32), sigma)
    return (n - n.mean()) / (n.std() + 1e-8)


def fbm(h, w, sigma, seed, octaves=4, gain=0.5):
    acc = np.zeros((h, w), np.float32)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        acc += amp * gauss_noise(h, w, max(sigma / (2 ** o), 0.6), seed * 31 + o)
        norm += amp * amp
        amp *= gain
    return acc / np.sqrt(norm)


def stain_mult(h, w, seed, coverage, sigma, inner, ring, inner_amt=0.5, ring_amt=0.55):
    """Multiplier for water/tea stains: a tinted inside and a darker tide line along the rim."""
    n = fbm(h, w, sigma, seed, 4)
    t = np.quantile(n, 1.0 - coverage)
    inside = smoothstep(t - 0.08, t + 0.08, n)
    tide = np.exp(-((n - t) / 0.05) ** 2)
    m = lerp(1.0, C(*inner), (inside * inner_amt)[..., None])
    return m * lerp(1.0, C(*ring), (tide * ring_amt)[..., None])


def normalize(n):
    return n / np.linalg.norm(n, axis=-1, keepdims=True)


def decode_normal(a):
    return normalize(a * 2.0 - 1.0)


def encode_normal(n):
    return normalize(n) * 0.5 + 0.5


def flatten_normal(n, amount):
    m = n.copy()
    m[..., :2] *= amount
    return normalize(m)


def blend_normals(a, b):
    """Whiteout blend of two tangent-space normals."""
    return normalize(np.stack([a[..., 0] + b[..., 0], a[..., 1] + b[..., 1], a[..., 2] * b[..., 2]], -1))


def height_normal(hmap, strength):
    dx = (np.roll(hmap, -1, 1) - np.roll(hmap, 1, 1)) * 0.5
    drow = (np.roll(hmap, -1, 0) - np.roll(hmap, 1, 0)) * 0.5
    dv = -drow  # image rows go down, texture V (and the OpenGL normal's green) goes up
    return normalize(np.stack([-dx * strength, -dv * strength, np.ones_like(hmap)], -1))


# ------------------------------------------------------------------ output

MANIFEST = []


def write_set(name, albedo, normal_src, metal, ao, smooth, tile, source, smoothness=1.0, normal_scale=1.0):
    d = os.path.join(OUT, name)
    os.makedirs(d, exist_ok=True)
    to_img(albedo).save(os.path.join(d, f'{name}_Albedo.jpg'), quality=92, subsampling=0)
    normal_path = os.path.join(d, f'{name}_Normal.jpg')
    if isinstance(normal_src, str):
        shutil.copyfile(normal_src, normal_path)  # untouched Poly Haven map
    else:
        to_img(encode_normal(normal_src)).save(normal_path, quality=95, subsampling=0)
    # Channels are resized one by one: resizing RGBA in one go premultiplies by alpha (= smoothness here).
    chans = [to_img(c, 'L').resize((MASK_SIZE, MASK_SIZE), Image.LANCZOS)
             for c in (np.broadcast_to(metal, ao.shape), ao, np.zeros_like(ao), smooth)]
    Image.merge('RGBA', chans).save(os.path.join(d, f'{name}_Mask.png'), optimize=True)
    MANIFEST.append({'name': name, 'source': source, 'tileU': tile[0], 'tileV': tile[1],
                     'smoothness': smoothness, 'normalScale': normal_scale})
    print('wrote', name)


def polyhaven(name, aid, tile, recolor=None, post=None, ao_bake=0.5, smoothness=1.0, normal_scale=1.0):
    d = os.path.join(SRC, aid)
    albedo = load(os.path.join(d, f'{aid}_diff_2k.jpg'))
    arm = load(os.path.join(d, f'{aid}_arm_2k.jpg'))
    ao, rough, metal = arm[..., 0], arm[..., 1], arm[..., 2]
    if recolor:
        albedo = recolor(albedo)
    albedo = albedo * lerp(1.0, ao, ao_bake)[..., None]
    if post:
        albedo = post(albedo)
    write_set(name, np.clip(albedo, 0, 1), os.path.join(d, f'{aid}_nor_gl_2k.jpg'), metal, ao, 1.0 - rough,
              (tile, tile), f'polyhaven:{aid}', smoothness, normal_scale)


# ------------------------------------------------------------------ recolours

def checker_recolor(dark, light, squares=4, grout=(150, 142, 136)):
    """Two-tone checker with `squares` squares per tile side: dark squares -> dark, light squares -> light.

    The grid phase is fitted by correlation (so dirt inside a square never flips its colour), dirt is kept as a
    luminance ratio and the grout lines stay neutral."""
    def f(a):
        g = lum(a)
        n = g.shape[0]
        s = n // squares
        L = g - g.mean()
        idx = np.arange(n)

        def wave(o):
            return np.where(((idx - o) // s) % 2 == 0, 1.0, -1.0).astype(np.float32)

        waves = np.stack([wave(o) for o in range(s)])        # all phases
        best = (0.0, 0, 0)
        for y0 in range(0, s, 2):
            corr = waves @ (wave(y0) @ L)                     # one value per x phase
            x0 = int(np.argmax(np.abs(corr)))
            if abs(corr[x0]) > abs(best[0]):
                best = (corr[x0], x0, y0)
        c, x0, y0 = best
        prod = wave(y0)[:, None] * wave(x0)[None, :]
        m = ((prod > 0) == (c > 0)).astype(np.float32)        # 1 = light square
        rx, ry = (idx - x0) % s, (idx - y0) % s
        edge = np.minimum(np.minimum(rx, s - rx)[None, :], np.minimum(ry, s - ry)[:, None]).astype(np.float32)
        gz = np.exp(-(edge / 3.0) ** 2)                        # grout zone
        inner = gz < 0.1
        base = lerp(g[(m < 0.5) & inner].mean(), g[(m >= 0.5) & inner].mean(), m)
        out = lerp(C(*dark), C(*light), m[..., None]) * (g / base)[..., None]
        out = lerp(out, C(*grout) * (g / g.mean())[..., None], (gz * 0.9)[..., None])
        return np.clip(out, 0, 1)
    return f


def ramp_recolor(dark, light, keep=0.0):
    """Maps luminance onto a two-colour ramp (keeps all the luminance detail)."""
    def f(a):
        g = lum(a)
        lo, hi = np.quantile(g, [0.02, 0.98])
        t = np.clip((g - lo) / (hi - lo), 0, 1)[..., None]
        return lerp(lerp(C(*dark), C(*light), t), a, keep)
    return f


def repaint(color, amount=0.85):
    """Recolours low-saturation paint and keeps the saturated chips (bare wood/rust) as they are."""
    def f(a):
        sat = a.max(-1) - a.min(-1)
        pm = (1.0 - smoothstep(0.06, 0.16, sat)) * amount
        g = lum(a)
        paint = C(*color) * (g / np.median(g))[..., None]
        return np.clip(lerp(a, paint, pm[..., None]), 0, 1)
    return f


def brighten(gamma):
    return lambda a: np.clip(a, 0, 1) ** gamma


def stained(seed, coverage, sigma, inner, ring, inner_amt=0.5, ring_amt=0.55):
    def f(a):
        h, w = a.shape[:2]
        return np.clip(a * stain_mult(h, w, seed, coverage, sigma, inner, ring, inner_amt, ring_amt), 0, 1)
    return f


def chain(*fs):
    def f(a):
        for g in fs:
            a = g(a)
        return a
    return f


# ------------------------------------------------------------------ wallpapers

WALLPAPERS = {
    # Dining hall: rose stripes with hearts, raspberry lower panel.
    'WallpaperRose': dict(A=(228, 166, 184), B=(246, 230, 220), pin=(212, 136, 158), motif='heart',
                          motif_color=(186, 70, 104), low=(126, 52, 76), low_line=(146, 66, 92),
                          border=(244, 230, 216), trim=(186, 70, 104), seed=11, peel=0.10),
    # Hallways: cream and mint stripes with pink polka dots, teal lower panel.
    'WallpaperCream': dict(A=(242, 230, 206), B=(190, 224, 206), pin=(150, 198, 176), motif='dot',
                           motif_color=(226, 140, 166), low=(98, 140, 130), low_line=(116, 158, 146),
                           border=(244, 232, 214), trim=(226, 140, 166), seed=23, peel=0.08),
    # Staff room: lilac stripes with yellow stars, plum lower panel.
    'WallpaperLilac': dict(A=(202, 184, 224), B=(238, 228, 242), pin=(172, 146, 204), motif='star',
                           motif_color=(236, 200, 110), low=(88, 68, 112), low_line=(104, 82, 130),
                           border=(240, 232, 242), trim=(172, 146, 204), seed=37, peel=0.11),
}


def draw_motif(d, kind, cx, cy, s, color):
    if kind == 'heart':
        t = np.linspace(0, 2 * np.pi, 72, endpoint=False)
        x = 16 * np.sin(t) ** 3
        y = 13 * np.cos(t) - 5 * np.cos(2 * t) - 2 * np.cos(3 * t) - np.cos(4 * t)
        d.polygon(list(zip(cx + x * s / 17.0, cy - y * s / 17.0)), fill=color)
    elif kind == 'star':
        pts = []
        for i in range(10):
            r = s if i % 2 == 0 else s * 0.45
            a = -np.pi / 2 + i * np.pi / 5
            pts.append((cx + r * np.cos(a), cy + r * np.sin(a)))
        d.polygon(pts, fill=color)
    else:
        r = s * 0.55
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color)


def draw_pattern(p):
    """The wallpaper as it looked new: drawn at 2x and downsampled for clean edges. Tiles horizontally."""
    S = 2
    W = H = SIZE * S
    ppm = SIZE / 3.0 * S

    def row(y_m):
        return int(round((3.0 - y_m) * ppm))

    im = Image.new('RGB', (W, H), p['A'])
    d = ImageDraw.Draw(im)
    main_bottom, top_low = row(1.06), row(0.94)
    period, band_a = 128 * S, 80 * S
    for k in range(W // period):
        x0 = k * period
        d.rectangle([x0 + band_a, 0, x0 + period - 1, main_bottom], fill=p['B'])
        for px in (x0 + band_a + 7 * S, x0 + period - 9 * S):
            d.rectangle([px, 0, px + 2 * S - 1, main_bottom], fill=p['pin'])
        step = 192 * S
        y = step // 2 + (step // 2 if k % 2 else 0)
        while y < main_bottom - 30 * S:
            draw_motif(d, p['motif'], x0 + band_a // 2, y, 20 * S, p['motif_color'])
            y += step
    # crown band under the ceiling
    d.rectangle([0, 0, W, row(2.93)], fill=p['low'])
    d.rectangle([0, row(2.93), W, row(2.93) + 3 * S], fill=p['trim'])
    # lower panel with fine vertical lines
    d.rectangle([0, top_low, W, H], fill=p['low'])
    for x in range(0, W, 64 * S):
        d.rectangle([x, top_low, x + S - 1, H], fill=p['low_line'])
    # lace border: band, scallops hanging into the lower panel, a row of dots, a line on top
    d.rectangle([0, main_bottom, W, top_low], fill=p['border'])
    r = 16 * S
    for x in range(0, W + 2 * r, 2 * r):
        d.ellipse([x - r, top_low - r, x + r, top_low + r], fill=p['border'])
    mid = (main_bottom + top_low) // 2
    for x in range(r, W, 2 * r):
        d.ellipse([x - 3 * S, mid - 3 * S, x + 3 * S, mid + 3 * S], fill=p['trim'])
    d.rectangle([0, main_bottom - 2 * S, W, main_bottom + S], fill=p['trim'])
    return np.asarray(im.resize((SIZE, SIZE), Image.LANCZOS), np.float32) / 255.0


def wallpaper(name, p):
    N = SIZE
    ppm = N / 3.0
    seed = p['seed']
    rows = np.arange(N, dtype=np.float32)[:, None]
    xs = np.arange(N, dtype=np.float32)[None, :]
    y_m = 3.0 - (rows + 0.5) / ppm  # height above the floor
    depth = 3.0 - y_m                # distance below the ceiling

    # plaster under the paper: worn_plaster_wall (1.8 m tile) squeezed to 1.5 m, so it tiles twice across 3 m
    d = os.path.join(SRC, 'worn_plaster_wall')
    pl_alb = np.tile(resize(load(os.path.join(d, 'worn_plaster_wall_diff_2k.jpg')), N // 2, N // 2), (2, 2, 1))
    pl_nrm = np.tile(resize(load(os.path.join(d, 'worn_plaster_wall_nor_gl_2k.jpg')), N // 2, N // 2), (2, 2, 1))
    pl_arm = np.tile(resize(load(os.path.join(d, 'worn_plaster_wall_arm_2k.jpg')), N // 2, N // 2), (2, 2, 1))
    pl_ao, pl_rough = pl_arm[..., 0], pl_arm[..., 1]

    # strip seams every 0.5 m
    strip = N / 6.0
    seam_d = np.minimum(xs % strip, strip - xs % strip)
    seam = np.exp(-(seam_d / 1.2) ** 2)
    seam_zone = np.exp(-(seam_d / 40.0) ** 2)

    # water running down from the ceiling
    rng = np.random.default_rng(seed + 5)
    col = blur1d(rng.standard_normal(N), 2.5)
    col = (col - col.mean()) / col.std()
    env = blur1d(rng.standard_normal(N), 140)
    env = (env - env.mean()) / env.std()
    length = (0.1 + 1.4 * np.clip(col * 0.5 + 0.3, 0, 1) ** 2.5 * smoothstep(-0.3, 1.2, env))[None, :]
    drip = smoothstep(length, length - 0.35, depth)
    tip = np.exp(-((depth - length) / 0.015) ** 2) * (length > 0.15)

    # peeling: blobs, more along the seams, under the ceiling, near the floor and where water ran
    field = (fbm(N, N, 80, seed + 1, 5) + 1.1 * seam_zone + 0.8 * smoothstep(0.7, 0.0, depth)
             + 0.5 * smoothstep(0.35, 0.0, y_m) + 0.6 * drip)
    t = np.quantile(field, 1.0 - p['peel'])
    peel = smoothstep(t - 0.04, t + 0.04, field)
    rim = np.clip(blur(peel, 2.0) * 3.0, 0, 1) * (1.0 - peel)        # torn paper edge
    shadow = blur(peel * (1.0 - np.roll(peel, 7, axis=0)), 2.0)        # under the hole's upper edge

    # the paper: faded, yellowed, blotchy
    paper = draw_pattern(p)
    paper = lerp(paper, lum(paper)[..., None], 0.22) * C(255, 243, 216)
    paper = paper * (1.0 + 0.07 * fbm(N, N, 110, seed + 2, 3))[..., None]
    paper = paper * (1.0 + 0.03 * gauss_noise(N, N, 0.8, seed + 3))[..., None]
    paper = paper * (1.0 - 0.18 * seam)[..., None]

    damp = pl_alb * C(205, 195, 180) * lerp(1.0, pl_ao, 0.6)[..., None]  # damp, darker plaster
    albedo = lerp(paper, damp, peel[..., None])
    albedo = lerp(albedo, C(222, 210, 188), (rim * 0.85)[..., None])
    albedo = albedo * (1.0 - 0.5 * shadow)[..., None]
    albedo = albedo * lerp(1.0, C(196, 166, 120), (drip * 0.55)[..., None])
    albedo = albedo * lerp(1.0, C(120, 96, 66), (tip * 0.5)[..., None])
    albedo = albedo * stain_mult(N, N, seed + 4, 0.07, 70, (210, 180, 130), (140, 110, 70))
    grime = smoothstep(0.0, 0.5, y_m + 0.12 * fbm(N, N, 30, seed + 6, 3))
    albedo = albedo * lerp(C(92, 78, 66), 1.0, grime[..., None])
    speck = blur((gauss_noise(N, N, 1.0, seed + 7) > 2.4).astype(np.float32), 0.8)
    speck = np.clip(speck * 2.0, 0, 1) * np.clip(smoothstep(0.9, 0.0, y_m) + drip, 0, 1)
    albedo = albedo * lerp(1.0, C(70, 80, 60), (speck * 0.7)[..., None])

    n_plaster = decode_normal(pl_nrm)
    normal = normalize(lerp(flatten_normal(n_plaster, 0.3), n_plaster, peel[..., None]))
    normal = blend_normals(normal, height_normal(blur(1.0 - peel, 1.2) + 0.8 * blur(rim, 1.5), 3.0))

    smooth = lerp(0.14 + 0.08 * drip, 1.0 - pl_rough, peel)
    ao = lerp(1.0, pl_ao, peel) * (1.0 - 0.3 * shadow)
    write_set(name, np.clip(albedo, 0, 1), normal, 0.0, ao, smooth, (3.0, 3.0),
              'original pattern over polyhaven:worn_plaster_wall')


# ------------------------------------------------------------------ the set list

def build_all(only):
    def want(n):
        return only is None or n in only

    # floors
    if want('FloorCheckerRose'):   # dining hall: rose and cream checker, worn
        polyhaven('FloorCheckerRose', 'floor_tiles_06', 1.6, recolor=checker_recolor((170, 112, 122), (232, 218, 200)))
    if want('FloorWoodWorn'):      # hallways, staff room
        polyhaven('FloorWoodWorn', 'wood_floor_worn', 2.0)
    if want('FloorParquet'):       # manager's office
        polyhaven('FloorParquet', 'herringbone_parquet', 3.4, smoothness=0.7)
    if want('FloorCheckerDiner'):  # kitchen: dirty black and cream checker
        polyhaven('FloorCheckerDiner', 'checkered_pavement_tiles', 2.0, recolor=ramp_recolor((26, 24, 26), (214, 204, 182)))
    if want('FloorMosaicMint'):    # restroom: mint mosaic
        polyhaven('FloorMosaicMint', 'square_tiles', 2.4, recolor=ramp_recolor((62, 92, 88), (182, 222, 208), keep=0.15))
    if want('FloorConcrete'):      # storage
        polyhaven('FloorConcrete', 'concrete_floor_worn_001', 3.0)
    if want('FloorStage'):
        polyhaven('FloorStage', 'dark_wooden_planks', 2.0)
    # walls
    for n, spec in WALLPAPERS.items():
        if want(n):
            wallpaper(n, spec)
    if want('WallPaintedConcrete'):  # kitchen: institutional green paint, peeling
        polyhaven('WallPaintedConcrete', 'painted_concrete', 2.0, post=brighten(0.8))
    if want('WallPlasterTeal'):      # restroom
        polyhaven('WallPlasterTeal', 'blue_plaster_weathered', 2.0)
    if want('WallBrickPainted'):     # storage
        polyhaven('WallBrickPainted', 'painted_brick', 1.8)
    if want('WallPanels'):           # manager's office
        polyhaven('WallPanels', 'wooden_panels', 2.1)
    if want('WallVelvet'):           # stage
        polyhaven('WallVelvet', 'velour_velvet', 0.5)
    if want('CeilingStained'):       # ceilings with leak stains
        polyhaven('CeilingStained', 'ceiling_interior', 3.0,
                  post=stained(91, 0.08, 110, (205, 178, 128), (128, 98, 60), 0.4, 0.6))
    # trim and props
    if want('TrimPainted'):          # pillars, door frames: chipped cream paint
        polyhaven('TrimPainted', 'distressed_painted_planks', 1.6, recolor=repaint((238, 226, 214)))
    if want('MetalSheetRusty'):      # elevator cars, breaker box, safe
        polyhaven('MetalSheetRusty', 'rusty_metal_sheet', 2.0)
    if want('MetalTread'):           # elevator floor
        polyhaven('MetalTread', 'metal_plate', 0.5)
    if want('MetalRustyPaint'):      # lockers (tinted pastel in the game)
        polyhaven('MetalRustyPaint', 'rusty_metal_02', 1.0)
    if want('WoodWorn'):             # cabinets, tables, chairs, desk
        polyhaven('WoodWorn', 'wood_table_worn', 1.1)
    if want('GinghamRose'):          # table cloths: pink gingham with tea stains
        polyhaven('GinghamRose', 'gingham_check', 0.45, recolor=ramp_recolor((150, 52, 82), (244, 232, 226)),
                  post=stained(57, 0.07, 150, (214, 176, 120), (150, 112, 70), 0.45, 0.5))


if __name__ == '__main__':
    SRC, OUT = sys.argv[1], sys.argv[2]
    only = set(sys.argv[3].split(',')) if len(sys.argv) > 3 else None
    os.makedirs(OUT, exist_ok=True)
    manifest_path = os.path.join(OUT, 'surfaces.json')
    previous = []
    if only is not None and os.path.exists(manifest_path):
        previous = [e for e in json.load(open(manifest_path, encoding='utf-8'))['surfaces'] if e['name'] not in only]
    build_all(only)
    entries = sorted(previous + MANIFEST, key=lambda e: e['name'])
    with open(manifest_path, 'w', encoding='utf-8') as f:
        json.dump({'surfaces': entries}, f, indent=2)
    print(f'{len(MANIFEST)} sets written, manifest has {len(entries)}')
