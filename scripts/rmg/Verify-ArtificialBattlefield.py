"""Native Battlefield matrix: fairness, parameter continuity and old package preservation.
Usage: python Verify-ArtificialBattlefield.py OUTPUT [--verify-only]
Historical replay cases use the local accepted artifact corpora.
"""
import argparse,copy,hashlib,json,os,subprocess,sys
from pathlib import Path
from importlib.util import module_from_spec, spec_from_file_location

sys.dont_write_bytecode=True
SPEC=spec_from_file_location('pvp_checks',Path(__file__).with_name('Verify-RegionsPvp.py'))
COMMON=module_from_spec(SPEC);SPEC.loader.exec_module(COMMON)
ROOT=COMMON.ROOT
LEVELS=('small','medium','high','extreme','ultra')


def schedule():
    base=dict(schema_version=12,preset='balanced',layout_family='artificial-battlefield',seed='397716241463670640',
        size='256,256',players=4,terrain_complexity='medium',block_shape='cut-corners',lane_width='standard',
        water_amount='standard',gravel_moss_amount='standard',neutral_colony_density='standard',original_surface_relations=True,
        prevent_colony_overlapping=True,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,100),starting_colony_shares=[0,10,20,30])
    cases=[]
    def add(name,**changes):
        s=copy.deepcopy(base);s.update(changes)
        if len(s['starting_colony_shares'])!=s['players']:s['starting_colony_shares']=[0]*s['players']
        cases.append(dict(id=name,settings=s))
    for shape in ('rectangles','cut-corners','diamonds'):
        for level in LEVELS:add(shape+'-'+level,block_shape=shape,terrain_complexity=level)
    for width in ('narrow','wide'):add('lanes-'+width,lane_width=width)
    for field,levels in (('water_amount',('low','high','extreme','ultra')),('gravel_moss_amount',('low','high','extreme','ultra')),('neutral_colony_density',('sparse','dense','extreme','ultra'))):
        for value in levels:add(field+'-'+value,**{field:value})
    for players in (2,4):
        for seed in ('0','1'):add(f'64-p{players}-s{seed}',size='64,64',players=players,seed=seed)
    for theme,seed in (('NORMAL','0'),('DESERT','1'),('SWAMP','18446744073709551615')):
        add('128-eight-'+theme,size='128,128',players=8,tileset=theme,seed=seed)
    add('two-horizontal',players=2,seed='1',block_shape='diamonds')
    for theme in ('DESERT','SWAMP','CANDY'):add('theme-'+theme,tileset=theme)
    add('512-eight',size='512,512',players=8,tileset='CANDY',starting_colony_shares=[100]*8,starting_colony_mode='random')
    for strict in (True,False):
        add('512-ultra-'+str(strict),size='512,512',players=8,terrain_complexity='ultra',water_amount='ultra',gravel_moss_amount='ultra',
            neutral_colony_density='ultra',prevent_colony_overlapping=strict,original_surface_relations=strict)
    add('free-surfaces',original_surface_relations=False)
    add('random-ownership',starting_colony_mode='random')
    add('wasps-only',neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0)|dict(wasps=100))
    add('no-colonies',neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0))
    cases.extend(c for c in COMMON.schedule() if 'baseline' in c)
    old=ROOT/'artifacts/rmg/natural-pvp/runtime-final'
    for name in ('pvp-desert-six','pvp-candy-eight'):
        report=json.loads((old/(name+'-report.json')).read_text())
        cases.append(dict(id='replay-'+name,settings=report['player_settings']['requested'],baseline=str(old/(name+'.oramap'))))
    old=ROOT/'artifacts/rmg/reassessment-comparison/regions-legacy-preservation'
    for name in ('artificial-battlefield-8300001','structured-competitive-8300001'):
        cases.append(dict(id='replay-'+name,settings=json.loads((old/'settings'/(name+'.json')).read_text(encoding='utf-8-sig')),baseline=str(old/'run-a/maps'/(name+'.oramap'))))
    return cases


def generate(folder,cases):
    env=os.environ.copy();env.update(DOTNET_ROOT=str(ROOT/'.tools/dotnet'),DOTNET_MULTILEVEL_LOOKUP='0',ENGINE_DIR='..',MOD_SEARCH_PATHS=str(ROOT/'mods')+',./mods')
    tool=str(ROOT/'engine/bin/OpenRA.Utility.exe')
    for case in cases:
        out=folder/case['id'];out.mkdir();(out/'settings.json').write_text(json.dumps(case['settings'],indent=2))
        run=subprocess.run([tool,'sa','--generate-sa-map',str(out/'map.oramap'),'--player-settings',str(out/'settings.json'),'--report',str(out/'report.json'),'--verify-repeatability'],cwd=ROOT/'engine',env=env,capture_output=True,timeout=180)
        (out/'generate.log').write_bytes(run.stdout+run.stderr)
        assert run.returncode==0,(case['id'],(run.stdout+run.stderr).decode(errors='replace')[-2500:])
        if 'baseline' not in case:
            run=subprocess.run([tool,'sa','--compare-natural-terrain','--reference',str(out/'map.oramap'),str(out/'actual')],cwd=ROOT/'engine',env=env,capture_output=True,timeout=180)
            (out/'export.log').write_bytes(run.stdout+run.stderr);assert run.returncode==0,case['id']
        print(case['id'],'generated, linted and repeated',flush=True)


def verify(folder,cases):
    records=[];maps={};objectives={}
    for case in cases:
        out=folder/case['id'];s=case['settings'];r=json.loads((out/'report.json').read_text())
        if 'baseline' in case:
            assert COMMON.members(out/'map.oramap')==COMMON.members(case['baseline']),case['id']
            continue
        assert r['native_validation']['accepted'] and r['performance']['repeatability_checked'] and r['package_validation']['map_yaml_lint']=='passed',case['id']
        g=r['regions'];v=r['native_validation'];policy=v.get('regions_policy',v)
        # Regions native reports flatten policy fields at the top level.
        assert policy['battlefield_ground_access'] and policy['battlefield_weighted_pool_parity'],case['id']
        assert all(v[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),case['id']
        native=(out/'actual/semantic.u8').read_bytes();n=int(s['size'].split(',')[0]);axes={2:1,4:2,8:4}[s['players']];seed=int(s['seed']);actors=COMMON.actors(out/'map.oramap')
        assert hashlib.sha256(native).hexdigest()==g['semantic_sha256'],case['id']
        for y in range(n):
            for x in range(n):assert all(native[y*n+x]==native[b*n+a] for a,b in COMMON.orbit(x,y,n,axes,seed)),(case['id'],x,y)
        for kind in {t for t,x,y in actors}:
            points={(x,y) for t,x,y in actors if t==kind}
            assert all(COMMON.orbit(x,y,n,axes,seed)<=points for x,y in points),(case['id'],kind)
        targets=[a for a in actors if a[0]=='mpspawn' or a[0].endswith('_colony')]
        colonies=[a for a in targets if a[0].endswith('_colony')]
        assert len(targets)-len(colonies)==s['players'] and len(colonies)%s['players']==0,case['id']
        assert all(s['neutral_colony_weights'][t.removesuffix('_colony')]>0 for t,x,y in colonies),case['id']
        maps[case['id']]=native;objectives[case['id']]=targets
        records.append(dict(id=case['id'],placed=len(colonies),target=g['neutral_colonies_requested'],generation_ms=r['performance']['logical_generation_ms'],water=g['metrics']['water_percent_map'],gravel=g['metrics']['gravel_percent_land'],moss=g['metrics']['moss_percent_land'],total_surface=g['metrics']['gravel_percent_land']+g['metrics']['moss_percent_land']))
    reference=objectives['cut-corners-medium']
    for case in cases:
        s=case['settings']
        if 'baseline' not in case and s['size']=='256,256' and s['players']==4 and s['seed']=='397716241463670640' and s['neutral_colony_weights']==dict.fromkeys(COMMON.KEYS,100) and s['neutral_colony_density']=='standard':
            assert objectives[case['id']]==reference,('terrain control moved planned colonies',case['id'])
    for theme in ('DESERT','SWAMP','CANDY'):assert maps['cut-corners-medium']==maps['theme-'+theme],theme
    for shape in ('rectangles','cut-corners','diamonds'):
        assert len({maps[shape+'-'+level] for level in LEVELS})==5,shape
    assert len({maps[shape+'-medium'] for shape in ('rectangles','cut-corners','diamonds')})==3
    assert len({maps[name] for name in ('cut-corners-medium','lanes-narrow','lanes-wide')})==3
    byid={c['id']:c for c in records}
    for field,key,levels in (('water_amount','water',('low','high','extreme','ultra')),('gravel_moss_amount','total_surface',('low','high','extreme','ultra')),('neutral_colony_density','placed',('sparse','dense','extreme','ultra'))):
        values=[byid[field+'-'+level][key] for level in levels];assert values==sorted(values),(field,values)
    result=dict(status='PASS',accepted_battlefields=len(records),exact_legacy_replays=sum('baseline' in c for c in cases),cases=records)
    (folder/'verification.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('output',type=Path);parser.add_argument('--verify-only',action='store_true');args=parser.parse_args();folder=args.output.resolve();cases=schedule()
    if not args.verify_only:
        folder.mkdir(parents=True,exist_ok=True)
        if any(folder.iterdir()):raise ValueError('Choose an empty output directory.')
        (folder/'manifest.json').write_text(json.dumps(cases,indent=2));generate(folder,cases)
    verify(folder,cases)
