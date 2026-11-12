# Ruido de valor y fractal en numpy (vectorizado), con semilla.
import numpy as np


def _hash(x, y, seed):
    h = (x.astype(np.int64) * 374761393 + y.astype(np.int64) * 668265263 + seed * 144665) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215.0


def valor(x, y, seed=0):
    x = np.asarray(x, float); y = np.asarray(y, float)
    xi = np.floor(x); yi = np.floor(y)
    fx = x - xi; fy = y - yi
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
    a = _hash(xi, yi, seed) * (1 - fx) + _hash(xi + 1, yi, seed) * fx
    b = _hash(xi, yi + 1, seed) * (1 - fx) + _hash(xi + 1, yi + 1, seed) * fx
    return a * (1 - fy) + b * fy


def fbm(x, y, octavas=5, seed=0, rug=0.5, lac=2.03):
    """Ruido fractal en [0, 1]."""
    x = np.asarray(x, float); y = np.asarray(y, float)
    s = np.zeros(np.broadcast(x, y).shape); amp = 0.5; tot = 0.0
    for i in range(octavas):
        s = s + valor(x, y, seed + i * 31) * amp
        tot += amp
        x = x * lac + 17.1; y = y * lac - 9.7
        amp *= rug
    return s / tot


def suave(a, b, x):
    t = np.clip((np.asarray(x, float) - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)
