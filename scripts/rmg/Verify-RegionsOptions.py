"""Verify Regions V14 quantities, strict-first placement and saved native maps.

Usage: python Verify-RegionsOptions.py OUTPUT_DIRECTORY [--verify-only]
Existing output is never overwritten. Uses the standard library only.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('regions_extended', Path(__file__).with_name('Verify-RegionsExtended.py'))
extended = importlib.util.module_from_spec(spec)
spec.loader.exec_module(extended)
read = extended.read


def schedule():
    manifest = []
    for size in (128, 256):
        groups = [('baseline', {})]
        for control, field in [('water', 'water_amount'), ('surface', 'gravel_moss_amount'), ('density', 'neutral_colony_density')]:
            for level in ('extreme', 'ultra'):
                groups.append((control + '-' + level, {field: level}))
        for level in ('extreme', 'ultra'):
            groups.append(('all-' + level, dict(water_amount=level, gravel_moss_amount=level,
                                               neutral_colony_density=level, terrain_complexity=level)))
        groups.append(('all-ultra-free-surfaces', dict(water_amount='ultra', gravel_moss_amount='ultra',
                      neutral_colony_density='ultra', terrain_complexity='ultra', original_surface_relations=False)))
        groups.append(('two-player-ultra', dict(players=2, seed='18446744073709551615', water_amount='ultra',
                      gravel_moss_amount='ultra', neutral_colony_density='ultra', terrain_complexity='ultra',
                      original_surface_relations=False)))
        for name, overrides in groups:
            settings = dict(schema_version=8, preset='balanced', layout_family='natural-landscape',
                            seed='397716241463670640', size=f'{size},{size}', players=4, terrain_complexity='medium',
                            water_amount='standard', gravel_moss_amount='standard', neutral_colony_density='standard',
                            original_surface_relations=True)
            settings.update(overrides)
            for prevent in (True, False):
                manifest.append(dict(id=f'{size}-{name}-{"strict" if prevent else "relaxed"}',
                                     settings=dict(settings, prevent_colony_overlapping=prevent)))
    return manifest


def actors(path):
    with zipfile.ZipFile(path) as archive:
        yaml = archive.read('map.yaml').decode('utf-8-sig')
    section = yaml.split('\nActors:\n', 1)[1].split('\nRules:', 1)[0]
    blocks, current = [], []
    for line in section.splitlines():
        if line.startswith('\t') and not line.startswith('\t\t'):
            if current: blocks.append('\n'.join(current))
            current = [line]
        elif current:
            current.append(line)
    if current: blocks.append('\n'.join(current))
    return [block for block in blocks if ': mpspawn' in block.split('\n')[0] or '_colony' in block.split('\n')[0]]


def verify(folder, manifest):
    reports, cells, summaries = {}, {}, []
    for case in manifest:
        name = case['id']
        out = folder / name
        report = read(out / 'report.json')
        ref = read(out / 'actual/reference.json')
        data = (out / 'actual/semantic.u8').read_bytes()
        native, regions = report['native_validation'], report['regions']
        assert read(out / 'settings.json') == case['settings'], name
        assert report['generator_version'] == 14 and native['accepted'], name
        assert report['performance']['repeatability_checked'], name
        assert report['package_validation']['map_yaml_lint'] == 'passed', name
        for field in ('footprint_overlap_cells', 'production_exit_failures', 'invalid_start_cells', 'invalid_colony_cells'):
            assert native[field] == 0, (name, field)
        assert len(ref['player_starts']) == case['settings']['players'], name
        assert len(ref['neutral_preview_positions']) == regions['neutral_colonies_placed'], name
        assert hashlib.sha256(data).hexdigest() == regions['semantic_sha256'], name
        assert hashlib.sha256((out / 'map.oramap').read_bytes()).hexdigest() == ref['source_package_sha256'], name
        assert regions['prevent_colony_overlapping'] == case['settings']['prevent_colony_overlapping'], name
        assert regions['neutral_colonies_placed'] == regions['neutral_colonies_strict'] + regions['neutral_colonies_fallback'], name
        if regions['prevent_colony_overlapping']:
            assert regions['neutral_overlapping_pairs'] == 0 and regions['neutral_colonies_fallback'] == 0, name
        land = len(data) - data.count(1)
        summaries.append(dict(id=name, water_percent=100*data.count(1)/len(data),
            gravel_percent_land=100*data.count(2)/land, moss_percent_land=100*data.count(3)/land,
            requested=regions['neutral_colonies_requested'], strict=regions['neutral_colonies_strict'],
            placed=regions['neutral_colonies_placed'], fallback=regions['neutral_colonies_fallback'],
            overlapping_pairs=regions['neutral_overlapping_pairs'], max_overlap_native=regions['maximum_neutral_overlap_native'],
            total_ms=report['performance']['total_ms']))
        reports[name], cells[name] = report, data
    for offset in range(0, len(manifest), 2):
        aid, bid = manifest[offset]['id'], manifest[offset+1]['id']
        a, b = reports[aid]['regions'], reports[bid]['regions']
        assert cells[aid] == cells[bid], (aid, 'overlap toggle changed terrain')
        assert a['neutral_colonies_placed'] == b['neutral_colonies_strict'], (aid, 'initial pass changed')
        assert b['neutral_colonies_placed'] >= a['neutral_colonies_placed'], aid
        aa, ba = actors(folder / aid / 'map.oramap'), actors(folder / bid / 'map.oramap')
        assert len(aa) == reports[aid]['players'] + a['neutral_colonies_placed'], (aid, 'actor reader')
        assert aa == ba[:len(aa)], (aid, 'strict placements changed')
        if b['neutral_colonies_fallback'] == 0:
            assert reports[aid]['actors_sha256'] == reports[bid]['actors_sha256'], (aid, 'unused fallback changed actors')
    for size in (128, 256):
        for control in ('water', 'surface'):
            a = next(row for row in summaries if row['id'] == f'{size}-{control}-extreme-strict')
            b = next(row for row in summaries if row['id'] == f'{size}-{control}-ultra-strict')
            fields = ('water_percent',) if control == 'water' else ('gravel_percent_land', 'moss_percent_land')
            for field in fields:
                assert b[field] - a[field] >= 5, (size, control, field, 'Ultra did not materially increase coverage')
        baseline = cells[f'{size}-baseline-strict']
        for level in ('extreme', 'ultra'):
            surface = cells[f'{size}-surface-{level}-strict']
            density = cells[f'{size}-density-{level}-strict']
            assert [v == 1 for v in surface] == [v == 1 for v in baseline], 'Surface Modifiers moved water'
            assert density == baseline, 'Colony density changed terrain'
    assert any(row['fallback'] > 0 for row in summaries), 'Matrix did not exercise the fallback'
    summary = dict(status='ALL_CASES_VERIFIED', cases=len(manifest),
        fallback_cases=sum(row['fallback'] > 0 for row in summaries),
        shortfall_cases=sum(row['placed'] < row['requested'] for row in summaries),
        max_total_ms=max(row['total_ms'] for row in summaries), measurements=summaries)
    (folder / 'verification.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
    print(f'Verified {len(manifest)} V14 maps: native validation, repeats, controls and strict-first fallback.', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('--verify-only', action='store_true')
    args = parser.parse_args()
    folder = args.output.resolve()
    if args.verify_only:
        manifest = read(folder / 'manifest.json')
    else:
        if folder.exists() and any(folder.iterdir()):
            parser.error('Output directory is not empty; choose a fresh directory.')
        folder.mkdir(parents=True, exist_ok=True)
        manifest = schedule()
        (folder / 'manifest.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
        extended.generate(folder, manifest)
    verify(folder, manifest)


if __name__ == '__main__':
    main()
