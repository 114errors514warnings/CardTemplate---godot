"""Read-only report of visible frame bounds and contact points."""

from pathlib import Path
import json
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'Resources/Images/Characters/FrameV2'

for character in ('swordmaster', 'isera'):
    body = np.asarray(Image.open(ASSETS / f'{character}_body.png').convert('RGBA'))[:, :, 3]
    rows = []
    for index in range(7):
        alpha = body[:, index * 256:(index + 1) * 256] > 64
        yy, xx = np.where(alpha)
        bottom = int(yy.max())
        foot_x = xx[yy >= bottom - 12]
        torso_x = xx[(yy >= 80) & (yy <= 160)]
        rows.append({
            'frame': index,
            'bbox': [int(xx.min()), int(yy.min()), int(xx.max()), bottom],
            'foot_min_x': int(foot_x.min()),
            'foot_max_x': int(foot_x.max()),
            'foot_mid_x': round((int(foot_x.min()) + int(foot_x.max())) / 2, 1),
            'torso_median_x': round(float(np.median(torso_x)), 1),
            'foot_baseline_delta': bottom - 228,
        })
    for row in rows:
        assert row['foot_baseline_delta'] == 0, f"{character} frame {row['frame']} misses baseline"
        if row['frame'] < 6:
            assert abs(row['foot_mid_x'] - 128) <= 6, f"{character} frame {row['frame']} foot drift"
            assert abs(row['torso_median_x'] - 128) <= 6, f"{character} frame {row['frame']} torso drift"
    print(character, json.dumps(rows, ensure_ascii=False))
print('FRAME_ANCHOR_AUDIT_PASS')
