"""Validate 512 Regions maps, report timings, and replay accepted smaller maps.
Usage: python Verify-Regions512.py OUTPUT_DIRECTORY [--verify-only]
Choose a fresh output directory. Uses the standard library and existing native export helpers.
"""
import argparse
import copy
import hashlib
import importlib.util
import json
import re
import sys
import zipfile
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('ownership', Path(__file__).with_name('Verify-RegionsOwnership.py'))
ownership = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ownership)

def schedule():
    cases = []
    base = dict(schema_version=10, preset='balanced', layout_family='natural-landscape',
        seed='397716241463670640', size='512,512', players=4, terrain_complexity='medium',
        water_amount='standard', gravel_moss_amount='standard', neutral_colony_density='standard',
        original_surface_relations=True, prevent_colony_overlapping=True,
        neutral_colony_weights=dict.fromkeys(ownership.players.KEYS, 100),
        starting_colony_shares=[0,10,20,30], starting_colony_mode='random')
    def add(name, **changes):
        settings=copy.deepcopy(base); settings.update(changes)
        cases.append(dict(id=name, settings=settings))
    for theme in ('NORMAL','DESERT','SWAMP','CANDY'):
        add('512-'+theme, **({} if theme=='NORMAL' else dict(tileset=theme)))
    for complexity in ('small','high','extreme','ultra'):
        add('512-complexity-'+complexity, terrain_complexity=complexity)
    add('512-solo-low', seed='18446744073709551615', players=1, starting_colony_shares=[50],
        terrain_complexity='small', water_amount='low', gravel_moss_amount='low', neutral_colony_density='sparse')
    add('512-eight-high', seed='0', players=8, starting_colony_shares=[100]*8, starting_colony_mode='closest-to-spawn',
        terrain_complexity='high', water_amount='high', gravel_moss_amount='high', neutral_colony_density='dense',
        neutral_colony_weights=dict(ants=0,beetles=0,scorpions=0,spiders=0,wasps=100))
    add('512-eight-extreme', seed='748797295927410807', players=8, starting_colony_shares=[10]*8,
        terrain_complexity='extreme', water_amount='extreme', gravel_moss_amount='extreme', neutral_colony_density='extreme',
        original_surface_relations=False, prevent_colony_overlapping=False)
    for strict in (True,False):
        add('512-eight-ultra-'+str(strict).lower(), players=8, starting_colony_shares=[100]*8,
            terrain_complexity='ultra', water_amount='ultra', gravel_moss_amount='ultra', neutral_colony_density='ultra',
            original_surface_relations=strict, prevent_colony_overlapping=strict)
    # Exact saved packages from the accepted all-biome checkpoint.
    baseline=ROOT/'artifacts/rmg/regions-biomes/native-01'
    for name in ('NORMAL-small','NORMAL-medium','NORMAL-high','DESERT-high','SWAMP-medium','CANDY-ultra'):
        settings=ownership.read(baseline/name/'settings.json')
        cases.append(dict(id='replay-'+name, settings=settings, baseline=str(baseline/name/'map.oramap')))
    return cases

def members(path):
    with zipfile.ZipFile(path) as z: return {n:z.read(n) for n in z.namelist()}

def verify(folder,cases):
    records=[]
    for case in cases:
        out=folder/case['id']; settings=case['settings']; report=ownership.read(out/'report.json')
        native=report['native_validation']; regions=report['regions']; ref=ownership.read(out/'actual/reference.json')
        assert native['accepted'] and report['package_validation']['map_yaml_lint']=='passed',case['id']
        assert report['performance']['repeatability_checked'],case['id']
        assert all(native[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),case['id']
        assert len(ref['player_starts'])==settings['players'],case['id']
        n=regions['neutral_colonies_placed']; shares=settings['starting_colony_shares']
        assert regions['starting_colonies_allocated_if_all_slots_occupied']==ownership.allocation(n,shares),case['id']
        actual=members(out/'map.oramap'); yaml=actual['map.yaml'].decode('utf-8-sig')
        if 'baseline' in case: assert actual==members(case['baseline']),case['id']
        else:
            assert 'MapSize: 516,516' in yaml and 'Bounds: 2,2,512,512' in yaml,case['id']
            assert len((out/'actual/semantic.u8').read_bytes())==512*512,case['id']
            assert hashlib.sha256((out/'actual/semantic.u8').read_bytes()).hexdigest()==regions['semantic_sha256'],case['id']
        records.append(dict(id=case['id'],target=regions['neutral_colonies_requested'],placed=n,
            logical_generation_ms=report['performance']['logical_generation_ms'],total_ms=report['performance']['total_ms']))
    normal=members(folder/'512-NORMAL/map.oramap')
    def geometry(data):
        actors=data['map.yaml'].decode('utf-8-sig').split('\nActors:\n',1)[1].split('\nRules:',1)[0]
        return re.sub(r'(?m)^\t(Actor[0-9]+): [^\r\n]+',r'\t\1: ACTOR',actors)
    for theme in ('DESERT','SWAMP','CANDY'):
        actual=members(folder/f'512-{theme}/map.oramap')
        assert actual['map.bin']==normal['map.bin'] and geometry(actual)==geometry(normal),theme
    result=dict(status='PASS',accepted=len(records),new_512=len(records)-6,exact_smaller_replays=6,
        cross_biome_geometry='identical',cases=records)
    (folder/'verification.json').write_text(json.dumps(result,indent=2))
    print('PASS:',len(records)-6,'512 maps; 6 exact smaller replays; biome geometry identical.',flush=True)
    for r in records: print(r,flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('output',type=Path); parser.add_argument('--verify-only',action='store_true')
    args=parser.parse_args(); folder=args.output.resolve(); cases=schedule()
    if not args.verify_only:
        folder.mkdir(parents=True,exist_ok=True)
        if any(folder.iterdir()): raise ValueError('Choose an empty output directory.')
        (folder/'manifest.json').write_text(json.dumps(cases,indent=2))
        ownership.generate(folder,cases)
    verify(folder,cases)
