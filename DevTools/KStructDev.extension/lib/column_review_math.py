# -*- coding: utf-8 -*-
"""Pure helpers for mapping a set of local 2D centerline points to a canvas."""

def fit_projection(lines, width, height, padding=18.0):
    lines = [line for line in lines if line]
    points = [p for line in lines for p in line]
    if not points or width <= 2 * padding or height <= 2 * padding:
        return []
    xmin = min(p[0] for p in points)
    xmax = max(p[0] for p in points)
    ymin = min(p[1] for p in points)
    ymax = max(p[1] for p in points)
    spanx = max(xmax - xmin, 1.0e-9)
    spany = max(ymax - ymin, 1.0e-9)
    scale = min((width - 2 * padding) / spanx, (height - 2 * padding) / spany)
    cx = (xmin + xmax) / 2.0
    cy = (ymin + ymax) / 2.0
    return [[(width / 2.0 + (p[0] - cx) * scale,
              height / 2.0 - (p[1] - cy) * scale) for p in line] for line in lines]
