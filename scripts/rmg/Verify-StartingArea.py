"""Native package regression for starting-area and fort ownership options across all current layouts."""
import argparse,copy,json,sys,re,os,subprocess
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from importlib.util import module_from_spec,spec_from_file_location
sys.dont_write_bytecode=True
SPEC=spec_from_file_location('strongholds',Path(__file__).with_name('Verify-Strongholds.py'))
S=module_from_spec(SPEC);SPEC.loader.exec_module(S)
ROOT=S.ROOT;COMMON=S.COMMON
FAMILIES=('natural-landscape','natural-landscape-pvp','artificial-battlefield','crossroads','ring','divided-lands','strongholds')
def schedule():
    base=dict(schema_version=17,preset='balanced',seed='825300756769842102',size='256,256',players=4,terrain_complexity='medium',water_amount='standard',gravel_moss_amount='standard',
        neutral_colony_density='ultra',original_surface_relations=False,prevent_colony_overlapping=False,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,100),starting_colony_shares=[0]*4)
    cases=[]
    def add(name,family,**changes):
        s=copy.deepcopy(base);s['layout_family']=family
        if family=='natural-landscape-pvp':s['mirroring_axes']=2
        s.update(changes);s['starting_colony_shares']=s.get('starting_colony_shares',[0]*s['players'])
        if len(s['starting_colony_shares'])!=s['players']:s['starting_colony_shares']=[0]*s['players']
        cases.append(dict(id=name,settings=s))
    for f in FAMILIES:
        for size,players in ((64,2),(128,8),(256,4),(512,8)):
            for strict in (True,False):
                for safe in (True,False):
                    changes=dict(size=f'{size},{size}',players=players,prevent_colony_overlapping=strict,respect_starting_safe_area=safe,
                        seed='825300756769842102' if size==256 else '1',terrain_complexity='medium' if size==256 else 'ultra',
                        water_amount='standard' if size==256 else 'low',gravel_moss_amount='ultra' if size!=256 else 'standard',original_surface_relations=strict)
                    if f=='natural-landscape-pvp':changes['mirroring_axes']=1 if players==2 else 2 if players==4 else 4
                    add(f'{f}-{size}-{strict}-{safe}',f,**changes)
        for theme in ('DESERT','SWAMP','CANDY'):
            add(f'{f}-{theme}',f,tileset=theme,respect_starting_safe_area=False)
        for species in COMMON.KEYS:
            add(f'{f}-{species}',f,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0)|{species:100},respect_starting_safe_area=False)
        add(f'{f}-empty',f,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0),respect_starting_safe_area=False)
    for castles in (True,False):
        for safe in (True,False):
            for own in (False,True):
                add(f'forts-{castles}-{safe}-{own}','strongholds',generate_castles=castles,respect_starting_safe_area=safe,own_starting_stronghold=own,starting_colony_shares=[0,10,20,30])
    # Every accepted family and historical snapshot stays byte-for-byte reproducible with default options.
    cases.extend(c for c in S.schedule() if 'baseline' in c)
    old=ROOT/'artifacts/rmg/strongholds/matrix-final'
    for name in ('complexity-True-medium','complexity-False-medium','density-True-False-ultra','size-64-4-True','size-512-8-True'):
        cases.append(dict(id='replay-strongholds-'+name,settings=json.loads((old/name/'settings.json').read_text()),baseline=str(old/name/'map.oramap')))
    return cases

def generate(folder,cases,workers):
    def run(c):
        if c['id'] not in ('natural-landscape-pvp-128-True-True','natural-landscape-pvp-128-True-False'):
            S.D.CR.BF.generate(folder,[c]);return
        out=folder/c['id'];out.mkdir();(out/'settings.json').write_text(json.dumps(c['settings']))
        env=os.environ.copy();env.update(DOTNET_ROOT=str(ROOT/'.tools/dotnet'),DOTNET_MULTILEVEL_LOOKUP='0',ENGINE_DIR='..',MOD_SEARCH_PATHS=str(ROOT/'mods')+',./mods')
        for i in range(2):
            run=subprocess.run([str(ROOT/'engine/bin/OpenRA.Utility.exe'),'sa','--generate-sa-map',str(out/'map.oramap'),'--player-settings',str(out/'settings.json')],cwd=ROOT/'engine',env=env,capture_output=True,timeout=180)
            log=run.stdout+run.stderr;(out/f'rejection-{i}.log').write_bytes(log)
            assert run.returncode and b'Terrain supports 0/8 starts in safe mirrored groups' in log,c['id']
        # Prove the same rejection with the accepted schema and default safe-area behavior.
        old=copy.deepcopy(c['settings']);old['schema_version']=11;old.pop('respect_starting_safe_area')
        (out/'old-settings.json').write_text(json.dumps(old))
        run=subprocess.run([str(ROOT/'engine/bin/OpenRA.Utility.exe'),'sa','--generate-sa-map',str(out/'old-map.oramap'),'--player-settings',str(out/'old-settings.json')],cwd=ROOT/'engine',env=env,capture_output=True,timeout=180)
        log=run.stdout+run.stderr;(out/'old-rejection.log').write_bytes(log)
        assert run.returncode and b'Terrain supports 0/8 starts in safe mirrored groups' in log,c['id']
        print(c['id'],'existing capacity rejection preserved',flush=True)
    with ThreadPoolExecutor(max_workers=workers) as pool:list(pool.map(run,cases))

def verify(folder,cases):
    records=[];historical=0;starts={};terrain={};actors={};intrusions={};reports={}
    for c in cases:
        name=c['id'];p=folder/name;s=c['settings']
        if 'baseline' in c:
            assert COMMON.members(p/'map.oramap')==COMMON.members(c['baseline']),name
            historical+=1;continue
        if name in ('natural-landscape-pvp-128-True-True','natural-landscape-pvp-128-True-False'):
            assert not (p/'map.oramap').exists() and all((p/f'rejection-{i}.log').exists() for i in range(2)) and (p/'old-rejection.log').exists(),name
            continue
        r=json.loads((p/'report.json').read_text());g=r['regions'];v=r['native_validation'];reports[name]=r
        assert v['accepted'] and r['performance']['repeatability_checked'] and r['package_validation']['map_yaml_lint']=='passed',name
        assert all(v[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),name
        a=COMMON.actors(p/'map.oramap');actors[name]=[x for x in a if x[0]=='mpspawn' or x[0].endswith('_colony')]
        starts[name]=[x for x in a if x[0]=='mpspawn'];assert len(starts[name])==s['players'],name
        terrain[name]=(p/'actual/semantic.u8').read_bytes()
        metrics=r['validation']['metrics'];inside=metrics.get('starting_safe_area_intrusions',0);intrusions[name]=inside
        if not s.get('respect_starting_safe_area',True):assert 'starting_safe_area_intrusions' in metrics,name
        own=s.get('own_starting_stronghold',False)
        text=COMMON.members(p/'map.oramap')['map.yaml'].decode('utf-8-sig')
        assert ('OwnStartingStronghold: True' in text)==own,name
        if own:
            plan=g['strongholds_plan'];expected=[a['fort']+1 if a['fort']<s['players'] else 0 for a in plan['colony_roles']]
            saved=[int(v) for v in re.search(r'ColonySpawnPoints: ([^\r\n]*)',text)[1].split(',') if v.strip()]
            assert saved==expected,name
        records.append(dict(id=name,colonies=g['neutral_colonies_placed'],inside_start_buffer=inside,generation_ms=r['performance']['logical_generation_ms']))
    for f in FAMILIES:
        for size in (64,128,256,512):
            for strict in (True,False):
                on=f'{f}-{size}-{strict}-True';off=f'{f}-{size}-{strict}-False'
                if on not in starts:
                    assert off not in starts;continue
                assert starts[on]==starts[off],(on,'toggle moved starts')
                if f!='artificial-battlefield':assert bytes(v==1 for v in terrain[on])==bytes(v==1 for v in terrain[off]),(on,'toggle moved water')
                if not strict and f!='artificial-battlefield':assert terrain[on]==terrain[off],(on,'free surfaces changed')
        assert intrusions[f'{f}-256-False-False']>0,(f,'no new sites in released buffer')
    for castles in (True,False):
        for safe in (True,False):
            off=f'forts-{castles}-{safe}-False';on=f'forts-{castles}-{safe}-True'
            assert actors[off]==actors[on] and terrain[off]==terrain[on],(on,'ownership changed geography')
    result=dict(status='PASS',new_maps=len(records),historical_replays=historical,preserved_capacity_rejections=2,worst_generation_ms=max(r['generation_ms'] for r in records),cases=records)
    (folder/'verification.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k!='cases'},indent=2))
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('output');p.add_argument('--verify-only',action='store_true');p.add_argument('--workers',type=int,default=4);args=p.parse_args()
    folder=Path(args.output).resolve();cases=schedule()
    if not args.verify_only:
        folder.mkdir();(folder/'cases.json').write_text(json.dumps(cases,indent=2));generate(folder,cases,args.workers)
    verify(folder,cases)
