"""Generate/reload/render all Regions biomes; verify semantics, geometry and safe capacity rejection.
Usage: python Verify-RegionsBiomes.py OUTPUT_DIRECTORY [--verify-only]
"""
import argparse
import importlib.util
import json
import re
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('ownership', Path(__file__).with_name('Verify-RegionsOwnership.py'))
ownership = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ownership)
read = ownership.read
THEMES = ('NORMAL', 'DESERT', 'SWAMP', 'CANDY')

def schedule():
    cases = []
    configs = [
        ('small',64,4,'small','low','low','sparse',True,True),
        ('medium',128,4,'medium','standard','standard','standard',True,True),
        ('high',256,8,'high','high','high','dense',False,True),
        ('extreme',128,4,'extreme','extreme','extreme','extreme',False,False),
        ('ultra',256,8,'ultra','ultra','ultra','ultra',False,False),
        ('capacity',64,4,'ultra','ultra','ultra','ultra',True,False),
    ]
    for theme in THEMES:
        for name,size,count,complexity,water,surface,density,original,spacing in configs:
            settings = dict(schema_version=10, preset='balanced', layout_family='natural-landscape',
                seed='397716241463670640', size=f'{size},{size}', players=count, terrain_complexity=complexity,
                water_amount=water, gravel_moss_amount=surface, neutral_colony_density=density,
                original_surface_relations=original, prevent_colony_overlapping=spacing,
                neutral_colony_weights=dict.fromkeys(ownership.players.KEYS,100),
                starting_colony_shares=[10*i for i in range(count)], starting_colony_mode='random')
            if name=='extreme': settings['neutral_colony_weights']=dict(ants=100,beetles=0,scorpions=0,spiders=0,wasps=0)
            if theme!='NORMAL': settings['tileset']=theme
            cases.append(dict(id=f'{theme}-{name}',settings=settings))
    return cases

def verify(folder,cases):
    accepted=[];rejected=[]
    def members(path):
        with zipfile.ZipFile(path) as z: return {n:z.read(n) for n in z.namelist()}
    def actor_geometry(yaml):
        actors=yaml.split('\nActors:\n',1)[1].split('\nRules:',1)[0]
        return re.sub(r'(?m)^\t(Actor[0-9]+): [^\r\n]+',r'\t\1: ACTOR',actors)
    for case in cases:
        out=folder/case['id'];base=folder/case['id'].replace(case['id'].split('-')[0]+'-','NORMAL-',1)
        report_path=out/'report.json'
        assert report_path.exists()==(base/'report.json').exists(),case['id']
        if not report_path.exists():
            assert (out/'generate.log').read_bytes()==(out/'rejection-repeat.log').read_bytes(),case['id']
            rejected.append(case['id']);continue
        report=read(report_path);native=report['native_validation']
        assert native['accepted'] and report['package_validation']['map_yaml_lint']=='passed',case['id']
        assert report['performance']['repeatability_checked'],case['id']
        assert (out/'actual/semantic.u8').read_bytes()==(base/'actual/semantic.u8').read_bytes(),case['id']
        assert ownership.players.options.actors(out/'map.oramap')==ownership.players.options.actors(base/'map.oramap'),case['id']
        actual=members(out/'map.oramap');normal=members(base/'map.oramap')
        yaml=actual['map.yaml'].decode('utf-8-sig');normal_yaml=normal['map.yaml'].decode('utf-8-sig')
        assert actual['map.bin']==normal['map.bin'],case['id']
        assert actor_geometry(yaml)==actor_geometry(normal_yaml),case['id']
        theme=case['settings'].get('tileset','NORMAL')
        assert 'Tileset: '+theme in yaml and 'ChoiceMode: Random' in yaml,case['id']
        if theme!='NORMAL':
            assert actual['map.png']!=normal['map.png'],case['id']
            assert (out/'actual/textures.png').read_bytes()!=(base/'actual/textures.png').read_bytes(),case['id']
            assert not any(name in yaml for name in ('plant_flower','rmg_plant_broad_leaf_grass','rmg_plant_brown_mushroom','rmg_plant_toad_stool')),case['id']
        if case['id'].endswith('-extreme'):
            assert all(': ants_colony' in block or ': mpspawn' in block for block in ownership.players.options.actors(out/'map.oramap')),case['id']
        accepted.append(dict(id=case['id'],colonies=report['regions']['neutral_colonies_placed']))
    summary=dict(status='PASS',accepted=len(accepted),safe_rejections=rejected,cases=accepted,
        geometry='identical map.bin, native semantics, starts, colonies and decorative positions across themes')
    (folder/'verification.json').write_text(json.dumps(summary,indent=2))
    print('PASS:',len(accepted),'native biome packages;',len(rejected),'repeated safe capacity rejections; identical geography.',flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('output',type=Path);parser.add_argument('--verify-only',action='store_true')
    args=parser.parse_args();folder=args.output.resolve();cases=schedule()
    if not args.verify_only:
        folder.mkdir(parents=True,exist_ok=True)
        if any(folder.iterdir()): raise ValueError('Choose an empty output directory.')
        (folder/'manifest.json').write_text(json.dumps(cases,indent=2))
        ownership.generate(folder,cases)
    verify(folder,cases)
