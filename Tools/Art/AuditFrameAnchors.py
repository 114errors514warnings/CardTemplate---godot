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

expanded = {
    'swordmaster_spear_equipped': 384,
    'swordmaster_bow_equipped': 256,
    'swordmaster_tome_equipped': 256,
    'isera_spear_equipped': 384,
    'isera_tome_equipped': 256,
    'isera_greatsword_equipped': 320,
}
for name, width in expanded.items():
    alpha = np.asarray(Image.open(ASSETS / f'{name}.png').convert('RGBA'))[:, :, 3]
    rows = []
    for index in range(7):
        yy, xx = np.where(alpha[:, index * width:(index + 1) * width] > 64)
        assert len(xx) > 0, f'{name} frame {index} is empty'
        torso = xx[(yy >= 110) & (yy <= 160)]
        assert len(torso) > 0, f'{name} frame {index} has no torso pixels'
        torso_x = float(np.median(torso))
        assert int(yy.max()) == 228, f'{name} frame {index} misses foot baseline'
        assert abs(torso_x - width / 2) <= 6, f'{name} frame {index} torso drift: {torso_x}'
        rows.append([index, int(yy.max()), round(torso_x, 1)])
    print(name, json.dumps(rows))
print('EQUIPMENT_FRAME_ANCHOR_AUDIT_PASS')

for weapon in ('short_sword', 'spear', 'bow', 'tome'):
    sheet = np.asarray(Image.open(ROOT / f'Resources/Images/Characters/FrameV3/swordmaster_{weapon}.png').convert('RGBA'))
    assert sheet.shape[:2] == (256, 384 * 11)
    poses = [sheet[:, i * 384:(i + 1) * 384] for i in range(11)]
    for index, pose in enumerate(poses):
        yy, xx = np.where(pose[:, :, 3] > 64)
        assert len(xx) > 0 and int(yy.max()) == 228, f'swordmaster {weapon} v3 frame {index} foot baseline'
        assert int(xx.min()) > 0 and int(xx.max()) < 383, f'swordmaster {weapon} v3 frame {index} clipped'
        if index < 7:
            torso = xx[(yy >= 100) & (yy <= 165)]
            assert len(torso) > 0 and abs(float(np.median(torso)) - 192) <= 6, f'swordmaster {weapon} v3 frame {index} torso drift'
    for index in range(7, 10):
        assert not np.array_equal(poses[index], poses[index + 1]), f'swordmaster {weapon} death frames {index} and {index+1} duplicate'
    print(f'SWORDMASTER_{weapon.upper()}_V3_ANCHOR_AUDIT_PASS')
