"""Generate and verify paired Regions V12 maps; use only the Python standard library.

Usage: python Verify-RegionsContinuity.py OUTPUT_DIRECTORY [--verify-only]
The manifest is saved before generation. Existing output is never overwritten.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import statistics
import subprocess

LEVELS = ('low', 'standard', 'high')
ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def schedule():
    groups = []
    for seed in ('397716241463670640', '16029658737383046505', '8973695974549234062', '0', '18446744073709551615'):
        for size in (128, 256):
            groups.append(dict(seed=seed, size=f'{size},{size}', players=4, water_amount='standard',
                               gravel_moss_amount='high' if seed == '397716241463670640' else 'standard',
                               neutral_colony_density='standard', original_surface_relations=True))
    for size in (128, 256):
        groups.append(dict(seed='7320260907001', size=f'{size},{size}', players=4, water_amount='high',
                           gravel_moss_amount='high', neutral_colony_density='dense', original_surface_relations=False))
        groups.append(dict(seed='7320260907002', size=f'{size},{size}', players=2, water_amount='low',
                           gravel_moss_amount='low', neutral_colony_density='sparse', original_surface_relations=True))
    return [dict(id=f'{i:02}-{group["size"].split(",")[0]}-{level}',
                 settings=dict(schema_version=6, preset='balanced', layout_family='natural-landscape',
                               terrain_complexity=level, **group))
            for i, group in enumerate(groups, 1) for level in LEVELS]


def generate(folder, manifest):
    env = os.environ.copy()
    env.update(DOTNET_ROOT=str(ROOT / '.tools/dotnet'), DOTNET_MULTILEVEL_LOOKUP='0',
               ENGINE_DIR='..', MOD_SEARCH_PATHS=str(ROOT / 'mods') + ',./mods')
    utility = ROOT / 'engine/bin/OpenRA.Utility.exe'
    for case in manifest:
        out = folder / case['id']
        out.mkdir()
        (out / 'settings.json').write_text(json.dumps(case['settings'], indent=2) + '\n', encoding='utf-8')
        commands = (
            ('generate', ['--generate-sa-map', str(out / 'map.oramap'), '--player-settings', str(out / 'settings.json'),
                          '--report', str(out / 'report.json'), '--verify-repeatability']),
            ('export', ['--compare-natural-terrain', '--reference', str(out / 'map.oramap'), str(out / 'actual')]))
        for label, args in commands:
            result = subprocess.run([str(utility), 'sa', *args], cwd=ROOT / 'engine', env=env,
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
            (out / f'{label}.log').write_bytes(result.stdout)
            if result.returncode:
                raise RuntimeError(f'{case["id"]}: {label} failed; see {out / (label + ".log")}')
        print(case['id'], 'generated', flush=True)


def correlation(left, right):
    lm, rm = statistics.mean(left), statistics.mean(right)
    numerator = sum((a - lm) * (b - rm) for a, b in zip(left, right))
    denominator = math.sqrt(sum((a - lm) ** 2 for a in left) * sum((b - rm) ** 2 for b in right))
    return numerator / denominator if denominator else None


def verify(folder, manifest):
    arrays, reports = {}, {}
    for case in manifest:
        out = folder / case['id']
        report = read(out / 'report.json')
        ref = read(out / 'actual/reference.json')
        cells = (out / 'actual/semantic.u8').read_bytes()
        assert read(out / 'settings.json') == case['settings'], case['id']
        assert report['generator_version'] == 12 and report['native_validation']['accepted'], case['id']
        assert report['performance']['repeatability_checked'], case['id']
        assert report['package_validation']['map_yaml_lint'] == 'passed', case['id']
        assert len(ref['player_starts']) == case['settings']['players'], case['id']
        assert len(ref['neutral_preview_positions']) == report['regions']['neutral_colonies_placed'], case['id']
        assert len(cells) == report['size'] ** 2, case['id']
        assert hashlib.sha256(cells).hexdigest() == report['regions']['semantic_sha256'], case['id']
        assert hashlib.sha256((out / 'map.oramap').read_bytes()).hexdigest() == ref['source_package_sha256'], case['id']
        arrays[case['id']], reports[case['id']] = cells, report
    comparisons = []
    for offset in range(0, len(manifest), 3):
        triple = manifest[offset:offset + 3]
        assert [case['settings']['terrain_complexity'] for case in triple] == list(LEVELS)
        for a, b in ((0, 1), (1, 2), (0, 2)):
            aid, bid = triple[a]['id'], triple[b]['id']
            left, right = arrays[aid], arrays[bid]
            size = reports[aid]['size']
            result = dict(before=aid, after=bid)
            for name, predicate in (('water', lambda v: v == 1), ('geology', lambda v: v >= 2), ('moss', lambda v: v == 3)):
                lm, rm = list(map(predicate, left)), list(map(predicate, right))
                result[name + '_retained_percent'] = 100 * sum(a and b for a, b in zip(lm, rm)) / max(1, sum(lm))
                coarse = lambda mask: [sum(mask[y * size + x] for y in range(by, by + 32) for x in range(bx, bx + 32))
                                       for by in range(0, size, 32) for bx in range(0, size, 32)]
                result[name + '_coarse_32_correlation'] = correlation(coarse(lm), coarse(rm))
            comparisons.append(result)
    summary = dict(status='ALL_CASES_VERIFIED', cases=len(manifest), comparisons=comparisons,
                   note='Geographic measurements support visual review; they are not runtime map rejection rules.')
    (folder / 'verification.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    print(f'Verified {len(manifest)} saved V12 maps, deterministic repeats, native terrain and actors.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('--verify-only', action='store_true', help='Verify an existing retained matrix without generating maps.')
    args = parser.parse_args()
    folder = args.output.resolve()
    if args.verify_only:
        manifest = read(folder / 'manifest.json')
    else:
        if folder.exists() and any(folder.iterdir()):
            parser.error('Output directory is not empty; choose a fresh directory.')
        folder.mkdir(parents=True, exist_ok=True)
        manifest = schedule()
        (folder / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        generate(folder, manifest)
    verify(folder, manifest)


if __name__ == '__main__':
    main()
