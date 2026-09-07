"""Generate and independently verify V15 native packages and frozen V14 replay.
Usage: python Verify-RegionsPlayers.py OUTPUT_DIRECTORY [--verify-only]
Uses the standard library. Existing output is never overwritten.
"""
import argparse
import hashlib
import importlib.util
import json
import os
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('options', Path(__file__).with_name('Verify-RegionsOptions.py'))
options = importlib.util.module_from_spec(spec)
spec.loader.exec_module(options)
read = options.read
KEYS = ('ants', 'beetles', 'scorpions', 'spiders', 'wasps')

def schedule():
    cases = []
    def add(name, size, **overrides):
        settings = dict(schema_version=9, preset='balanced', layout_family='natural-landscape',
            seed='397716241463670640', size=f'{size},{size}', players=4, terrain_complexity='medium',
            water_amount='standard', gravel_moss_amount='standard', neutral_colony_density='standard',
            original_surface_relations=True, prevent_colony_overlapping=True,
            neutral_colony_weights=dict.fromkeys(KEYS, 100))
        settings.update(overrides)
        cases.append(dict(id=f'{size}-{name}', settings=settings))
    for size in (128, 256):
        for players in range(1, 9): add(f'players-{players}', size, players=players)
        add('solo-empty', size, players=1, neutral_colony_weights=dict.fromkeys(KEYS, 0))
        variants = [(key, [1000 if k == key else 0 for k in KEYS]) for key in KEYS]
        variants += [('zero', [0]*5), ('mixed', [45,45,0,10,0]), ('equal', [100]*5)]
        for name, values in variants:
            for prevent in (True, False):
                add(f'weights-{name}-{"strict" if prevent else "relaxed"}', size,
                    neutral_colony_weights=dict(zip(KEYS, values)), neutral_colony_density='ultra',
                    prevent_colony_overlapping=prevent)
        for original in (True, False):
            for prevent in (True, False):
                add(f'stress-8-R{original}-O{prevent}', size, players=8, water_amount='ultra',
                    gravel_moss_amount='ultra', terrain_complexity='ultra', neutral_colony_density='ultra',
                    original_surface_relations=original, prevent_colony_overlapping=prevent)
    # Known constrained geography: all-Ultra 128 with dirt-only starts fits five,
    # not eight. This must reject deterministically without saving an unsafe map.
    for case in cases:
        if case['id'].startswith('128-stress-8-RTrue-'):
            case['expected_rejection'] = 'The completed terrain supports only 5/8 locally valid starts.'
    return cases


def generate(folder, manifest, resume=False):
    env=os.environ.copy()
    env.update(DOTNET_ROOT=str(ROOT/'.tools/dotnet'), DOTNET_MULTILEVEL_LOOKUP='0',
               ENGINE_DIR='..', MOD_SEARCH_PATHS=str(ROOT/'mods')+',./mods')
    for case in manifest:
        out=folder/case['id']
        if 'expected_rejection' in case:
            out.mkdir(exist_ok=resume)
            (out/'settings.json').write_text(json.dumps(case['settings'],indent=2)+'\n')
            for attempt in (1,2):
                result=subprocess.run([str(ROOT/'engine/bin/OpenRA.Utility.exe'),'sa','--generate-sa-map',str(out/'map.oramap'),
                    '--player-settings',str(out/'settings.json'),'--report',str(out/'report.json')],cwd=ROOT/'engine',env=env,
                    stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=120)
                (out/f'rejection-{attempt}.log').write_bytes(result.stdout)
                assert result.returncode and case['expected_rejection'] in result.stdout.decode('utf-8-sig'), case['id']
                assert not (out/'map.oramap').exists() and not (out/'report.json').exists(), case['id']
            print(case['id'],'safely rejected twice',flush=True)
        elif resume and (out/'report.json').exists() and not (out/'actual/reference.json').exists():
            assert read(out/'settings.json')==case['settings'],case['id']
            result=subprocess.run([str(ROOT/'engine/bin/OpenRA.Utility.exe'),'sa','--compare-natural-terrain',
                '--reference',str(out/'map.oramap'),str(out/'actual')],cwd=ROOT/'engine',env=env,
                stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=120)
            (out/'export-resumed.log').write_bytes(result.stdout)
            assert result.returncode==0,case['id']
        elif resume and (out/'report.json').exists() and (out/'actual/reference.json').exists():
            assert read(out/'settings.json')==case['settings'],case['id']
        else:
            options.extended.generate(folder,[case])

def verify(folder, manifest):
    reports, refs, cells, summaries = {}, {}, {}, []
    for case in manifest:
        name, settings = case['id'], case['settings']
        out = folder / name
        if 'expected_rejection' in case:
            for attempt in (1,2): assert case['expected_rejection'] in (out/f'rejection-{attempt}.log').read_text(), name
            assert not (out/'map.oramap').exists() and not (out/'report.json').exists(), name
            continue
        report, ref = read(out/'report.json'), read(out/'actual/reference.json')
        data = (out/'actual/semantic.u8').read_bytes()
        regions, native = report['regions'], report['native_validation']
        assert read(out/'settings.json') == settings, name
        assert report['generator_version'] == 15 and native['accepted'], name
        assert report['performance']['repeatability_checked'], name
        assert report['package_validation']['map_yaml_lint'] == 'passed', name
        assert len(ref['player_starts']) == settings['players'], name
        assert len(ref['neutral_preview_positions']) == regions['neutral_colonies_placed'], name
        assert hashlib.sha256(data).hexdigest() == regions['semantic_sha256'], name
        assert hashlib.sha256((out/'map.oramap').read_bytes()).hexdigest() == ref['source_package_sha256'], name
        for field in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells'):
            assert native[field] == 0, (name, field)
        weights = settings['neutral_colony_weights']
        assert regions['neutral_colony_weights'] == weights, name
        placed, drawn = regions['neutral_colonies_placed_by_type'], regions['neutral_colonies_drawn_by_type']
        assert sum(placed.values()) == regions['neutral_colonies_placed'], name
        assert sum(drawn.values()) == regions['neutral_colonies_requested'], name
        for key, weight in weights.items():
            assert placed[key] <= drawn[key], (name, 'substituted species')
            if weight == 0: assert placed[key] == drawn[key] == 0, (name,key)
        if not sum(weights.values()):
            assert regions['neutral_colonies_placed'] == regions['neutral_colonies_requested'] == 0, name
        if settings['prevent_colony_overlapping']:
            assert regions['neutral_overlapping_pairs'] == regions['neutral_colonies_fallback'] == 0, name
        reports[name], refs[name], cells[name] = report, ref, data
        summaries.append(dict(id=name, players=settings['players'], target=regions['neutral_colonies_requested'],
            placed=regions['neutral_colonies_placed'], by_type=placed, drawn_by_type=drawn,
            fallback=regions['neutral_colonies_fallback'], total_ms=report['performance']['total_ms']))
    for size in (128,256):
        group = [c for c in manifest if c['id'].startswith(f'{size}-weights-')]
        baseline = f'{size}-weights-equal-strict'
        for case in group:
            name=case['id']
            assert cells[name] == cells[baseline], (name,'weights moved terrain')
            assert refs[name]['player_starts'] == refs[baseline]['player_starts'], (name,'weights moved starts')
            if name.endswith('-strict'):
                other = name[:-7] + '-relaxed'
                strict, relaxed = options.actors(folder/name/'map.oramap'), options.actors(folder/other/'map.oramap')
                assert strict == relaxed[:len(strict)], (name, 'strict placement changed')
                assert reports[name]['regions']['neutral_colonies_drawn_by_type'] == reports[other]['regions']['neutral_colonies_drawn_by_type'], name
        assert refs[f'{size}-solo-empty']['player_starts'] == refs[f'{size}-players-1']['player_starts'], 'Solo empty moved start'
        frozen = ROOT / f'artifacts/rmg/regions-v14/native-matrix/{size}-baseline-strict'
        assert cells[f'{size}-players-4'] == (frozen/'actual/semantic.u8').read_bytes(), 'V15 changed V14 baseline terrain'
        assert refs[f'{size}-players-4']['player_starts'] == read(frozen/'actual/reference.json')['player_starts'], 'V15 changed V14 baseline starts'

    assert any(s['fallback'] for s in summaries), 'No fallback exercised'
    summary=dict(status='ALL_CASES_VERIFIED', cases=len(manifest), native_packages=len(summaries), safe_capacity_rejections=len(manifest)-len(summaries),
        shortfall_cases=sum(s['placed']<s['target'] for s in summaries), max_total_ms=max(s['total_ms'] for s in summaries), measurements=summaries)
    (folder/'verification.json').write_text(json.dumps(summary,indent=2)+'\n')
    print(f'Verified {len(summaries)} V15 native packages and {len(manifest)-len(summaries)} safe capacity rejections.',flush=True)

def replay(folder):
    frozen=ROOT/'artifacts/rmg/regions-v14/native-matrix'
    manifest=read(frozen/'manifest.json')
    folder.mkdir()
    (folder/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    options.extended.generate(folder,manifest)
    results=[]
    for case in manifest:
        name=case['id']
        old,new=read(frozen/name/'report.json'),read(folder/name/'report.json')
        # Compare every hash exposed by the frozen report, including the native package hash.
        hashes=[key for key in old if 'sha256' in key]
        assert hashes, 'Frozen report has no hashes'
        assert all(old[key]==new[key] for key in hashes), name
        assert (frozen/name/'actual/semantic.u8').read_bytes()==(folder/name/'actual/semantic.u8').read_bytes(), name
        results.append(dict(id=name,unchanged_hashes=hashes))
    (folder/'verification.json').write_text(json.dumps(dict(status='FROZEN_V14_UNCHANGED',cases=len(results),results=results),indent=2)+'\n')
    print(f'Verified {len(results)} exact frozen V14 replays.',flush=True)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output',type=Path)
    parser.add_argument('--verify-only',action='store_true')
    parser.add_argument('--resume',action='store_true')
    parser.add_argument('--replay-v14',action='store_true')
    args=parser.parse_args()
    folder=args.output.resolve()
    if args.verify_only:
        verify(folder,read(folder/'manifest.json'))
    else:
        if not args.resume and folder.exists() and any(folder.iterdir()): parser.error('Choose a fresh output directory.')
        folder.mkdir(parents=True,exist_ok=True)
        manifest=schedule()
        (folder/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
        generate(folder,manifest,args.resume)
        verify(folder,manifest)
    if args.replay_v14: replay(folder/'v14-replay')

if __name__=='__main__': main()
