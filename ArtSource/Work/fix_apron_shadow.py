import sys, numpy as np
from PIL import Image
from scipy import ndimage as ndi
src, dst, preview = sys.argv[1], sys.argv[2], sys.argv[3]
im = np.asarray(Image.open(src).convert('RGBA')).astype(np.float32)
rgb = im[..., :3]; lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
H, W = lum.shape
OX, OY = 560, 180                      # crop origin used while inspecting
yy, xx = np.mgrid[0:H, 0:W]; cx, cy = xx - OX, yy - OY
# Apron island: bright connected region containing the apron centre.
lab, n = ndi.label(lum > 95)
island = lab == lab[OY + 450, OX + 450]
# Protect the hem stripe (near the lower boundary) and the bow's shadow.
edge = island & ~ndi.binary_erosion(island, iterations=1)
lower_edge = edge & (cy > 460)
dist_lower = ndi.distance_transform_edt(~lower_edge)
protect = (dist_lower < 115) | ((cx > 585) & (cy > 555))
work = island & ~protect
# Local white reference: max of the lightly blurred luminance over a wide window.
blur = ndi.gaussian_filter(lum, 2)
ref = ndi.maximum_filter(np.where(island, blur, 0), size=181)
sat = rgb[..., 0] - rgb[..., 2]
shadow = work & ((lum < ref * 0.94) | (sat > 8))
shadow = ndi.binary_opening(shadow, iterations=1)
shadow = ndi.binary_dilation(shadow, iterations=6) & island & ~protect
# Normalised convolution fill from the clean apron pixels.
valid = (island & ~shadow).astype(np.float32)
fill = np.stack([ndi.gaussian_filter(rgb[..., c] * valid, 30) for c in range(3)], -1)
wsum = ndi.gaussian_filter(valid, 30)[..., None]
fill = fill / np.maximum(wsum, 1e-4)
alpha = ndi.gaussian_filter(shadow.astype(np.float32), 1.5)[..., None] * island[..., None]
out = rgb * (1 - alpha) + fill * alpha
res = np.concatenate([out, im[..., 3:]], -1).clip(0, 255).astype(np.uint8)
Image.fromarray(res, 'RGBA').save(dst)
Image.fromarray(res[OY:OY + 900, OX:OX + 940, :3]).save(preview)
print('shadow px', int(shadow.sum()), 'island px', int(island.sum()))
