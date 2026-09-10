"""Strongholds native maps: asymmetric forts, controls, density, continuity and saved-map replay."""
import argparse,copy,hashlib,json,math,sys
from collections import deque,defaultdict
from pathlib import Path
from importlib.util import module_from_spec,spec_from_file_location
sys.dont_write_bytecode=True
SPEC=spec_from_file_location('divided',Path(__file__).with_name('Verify-DividedLands.py'))
D=module_from_spec(SPEC);SPEC.loader.exec_module(D)
ROOT=D.ROOT;COMMON=D.COMMON;LEVELS=D.LEVELS;QUANTITIES=('low','standard','high','extreme','ultra')

def schedule(wide=False):
    base=dict(schema_version=16,preset='balanced',layout_family='strongholds',seed='825300756769842102',size='256,256',players=4,
        terrain_complexity='medium',water_amount='standard',gravel_moss_amount='standard',neutral_colony_density='standard',generate_castles=True,
        original_surface_relations=True,prevent_colony_overlapping=True,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,100),starting_colony_shares=[0,10,20,30])
    cases=[]
    def add(name,**changes):
        s=copy.deepcopy(base);s.update(changes)
        if len(s['starting_colony_shares'])!=s['players']:s['starting_colony_shares']=[0]*s['players']
        cases.append(dict(id=name,settings=s))
    for size in (64,128,256,512):
        for p in range(1,5 if size==64 else 9):
            for castles in (False,True):add(f'size-{size}-{p}-{castles}',size=f'{size},{size}',players=p,generate_castles=castles)
    for castles in (False,True):
        for level in LEVELS:add(f'complexity-{castles}-{level}',generate_castles=castles,terrain_complexity=level)
        for water in QUANTITIES:add(f'water-{castles}-{water}',generate_castles=castles,water_amount=water)
        for surface in QUANTITIES:add(f'surface-{castles}-{surface}',generate_castles=castles,gravel_moss_amount=surface)
        for original in (False,True):
            for density in ('sparse','standard','dense','extreme','ultra'):
                add(f'density-{castles}-{original}-{density}',generate_castles=castles,original_surface_relations=original,prevent_colony_overlapping=False,
                    neutral_colony_density=density,water_amount='ultra',gravel_moss_amount='ultra',terrain_complexity='ultra')
    for theme in ('DESERT','SWAMP','CANDY'):add('theme-'+theme,tileset=theme)
    for species in COMMON.KEYS:add('only-'+species,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0)|{species:100})
    add('no-colonies',neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0))
    add('random-ownership',starting_colony_mode='random')
    add('weighted-ownership',starting_colony_shares=[40,40,80,0])
    if wide:
        for seed in ('0','1','18446744073709551615'):
            for size,p in ((64,4),(128,8),(256,3),(256,7),(512,8)):
                for castles in (False,True):
                    for level in ('small','ultra'):
                        add(f'stress-{seed}-{size}-{p}-{castles}-{level}',seed=seed,size=f'{size},{size}',players=p,generate_castles=castles,terrain_complexity=level,
                            water_amount='ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',original_surface_relations=False,prevent_colony_overlapping=False)
        for castles in (False,True):
            for water in QUANTITIES:
                for surface in QUANTITIES:
                    add(f'quantities-{castles}-{water}-{surface}',generate_castles=castles,terrain_complexity='ultra',water_amount=water,gravel_moss_amount=surface,
                        neutral_colony_density='ultra',original_surface_relations=False,prevent_colony_overlapping=False)
        for seed in ('17','92451','9999999'):
            for p in (3,5,7,8):add(f'spread-{seed}-{p}',seed=seed,players=p)
    cases.extend(c for c in D.schedule() if 'baseline' in c)
    old=ROOT/'artifacts/rmg/divided-lands-shore/matrix-final'
    for name in ('p4-one-medium','p8-two-ultra','64-p4','128-p8','512-ultra-False','shore-review-none-ultra'):
        cases.append(dict(id='replay-divided-'+name,settings=json.loads((old/name/'settings.json').read_text()),baseline=str(old/name/'map.oramap')))
    return cases

def verify(folder,cases):
    maps={};objectives={};plans={};records=[];historical=0
    for c in cases:
        name=c['id'];s=c['settings'];p=folder/name
        if 'baseline' in c:
            assert COMMON.members(p/'map.oramap')==COMMON.members(c['baseline']),name
            historical+=1;continue
        r=json.loads((p/'report.json').read_text());g=r['regions'];v=r['native_validation'];plan=g['strongholds_plan'];n=int(s['size'].split(',')[0])
        assert v['accepted'] and r['performance']['repeatability_checked'] and r['package_validation']['map_yaml_lint']=='passed',name
        assert v['strongholds_connected'] and v['strongholds_blocked_entrance_cells']==0,name
        assert all(v[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),name
        assert all(x>=2 for x in v['strongholds_shore_clearances_native']),name
        assert g['symmetry_requirement']=='NOT_REQUIRED' and g['mirroring_axes']==0,name
        data=(p/'actual/semantic.u8').read_bytes();assert len(data)==n*n and hashlib.sha256(data).hexdigest()==g['semantic_sha256'],name
        # Prove the actual map is not reflected or rotationally replicated.
        assert data!=b''.join(data[y*n:(y+1)*n][::-1] for y in range(n)),name
        assert data!=b''.join(data[y*n:(y+1)*n] for y in reversed(range(n))),name
        assert data!=data[::-1],name
        actors=COMMON.actors(p/'map.oramap');targets=[a for a in actors if a[0]=='mpspawn' or a[0].endswith('_colony')]
        assert sum(t=='mpspawn' for t,x,y in targets)==s['players'],name
        assert plan['strongholds']==s['players'] and plan['generate_castles']==s['generate_castles'],name
        assert s['generate_castles'] or plan['castles']==0,name
        assert sum(f['castle'] for f in plan['forts'])==plan['castles'],name
        assert all(s['neutral_colony_weights'][t.removesuffix('_colony')]>0 for t,x,y in targets if t!='mpspawn'),name
        assert len(plan['colony_roles'])==g['neutral_colonies_placed'],name
        # Check the materialized road centerline, independently of the generator's policy report.
        for ax,ay,bx,by in plan['roads']:
            steps=max(abs(bx-ax),abs(by-ay))*2+1
            for i in range(steps+1):
                x=round(ax+(bx-ax)*i/steps);y=round(ay+(by-ay)*i/steps)
                if 0<=x<n and 0<=y<n:assert data[y*n+x]!=1,(name,'wet entrance',x,y)
        # Every objective is in the same terrain-only native component.
        start=next(y*n+x for t,x,y in targets if t=='mpspawn');seen=bytearray(n*n);seen[start]=1;queue=deque([start])
        while queue:
            i=queue.popleft();x=i%n;y=i//n
            for ny in range(max(0,y-1),min(n,y+2)):
                for nx in range(max(0,x-1),min(n,x+2)):
                    j=ny*n+nx
                    if not seen[j] and data[j]!=1:seen[j]=1;queue.append(j)
        assert all(seen[y*n+x] for t,x,y in targets),name
        maps[name]=data;objectives[name]=targets;plans[name]=plan
        records.append(dict(id=name,colonies=g['neutral_colonies_placed'],target=g['neutral_colonies_requested'],castles=plan['castles'],water=g['metrics']['water_percent_map'],
            surface=g['metrics']['gravel_percent_land']+g['metrics']['moss_percent_land'],generation_ms=r['performance']['logical_generation_ms']))
    comparisons=0
    for castles in (False,True):
        for original in (False,True):
            names=[f'density-{castles}-{original}-{d}' for d in ('sparse','standard','dense','extreme','ultra')]
            before=maps[names[0]]
            for name in names[1:]:
                assert bytes(v==1 for v in maps[name])==bytes(v==1 for v in before),name
                if not original:assert maps[name]==before,name
                comparisons+=1
        names=[f'complexity-{castles}-{l}' for l in LEVELS]
        assert all(objectives[x]==objectives[names[0]] for x in names[1:]),'complexity moved colonies'
        assert len({maps[x] for x in names})==5,'inert complexity'
        changed=sum(a!=b for a,b in zip(maps[names[0]],maps[names[-1]]))/len(maps[names[0]])
        assert changed>.025,('complexity weak response',castles,changed)
        for prefix in ('water','surface'):
            names=[f'{prefix}-{castles}-{l}' for l in QUANTITIES]
            assert all(objectives[x]==objectives[names[0]] for x in names[1:]),(prefix,'moved colonies')
            values=[next(r['water' if prefix=='water' else 'surface'] for r in records if r['id']==x) for x in names]
            assert values[-1]-values[0]>12,(prefix,'weak response',values)
    base=objectives['complexity-True-medium']
    for theme in ('DESERT','SWAMP','CANDY'):assert objectives['theme-'+theme]==base and maps['theme-'+theme]==maps['complexity-True-medium'],theme
    for mode in ('random-ownership','weighted-ownership'):assert objectives[mode]==base and maps[mode]==maps['complexity-True-medium'],mode
    assert plans['complexity-True-medium']['castles']>0 and maps['complexity-True-medium']!=maps['complexity-False-medium'],'castles inert'
    assert [a for a in base if a[0]=='mpspawn']==[a for a in objectives['complexity-False-medium'] if a[0]=='mpspawn'],'castle switch moved starts'
    # Mixed default weights must visibly prioritize front-line production and rear wasps.
    roles=plans['complexity-True-medium']['colony_roles'];means={}
    for species in COMMON.KEYS:
        positions=[r['front_fraction'] for r in roles if r['type']==species+'_colony'];assert positions,species
        means[species]=sum(positions)/len(positions)
    assert min(means['ants'],means['beetles'])>max(means['spiders'],means['scorpions'])>means['wasps'],means
    result=dict(status='PASS',strongholds_maps=len(records),historical_replays=historical,density_invariance_comparisons=comparisons,default_role_front_fractions=means,
        worst_generation_ms=max(r['generation_ms'] for r in records),cases=records)
    (folder/'verification.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k!='cases'},indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('output');parser.add_argument('--wide',action='store_true');parser.add_argument('--verify-only',action='store_true');parser.add_argument('--workers',type=int,default=4);args=parser.parse_args()
    folder=Path(args.output).resolve();cases=schedule(args.wide)
    if not args.verify_only:
        folder.mkdir();(folder/'cases.json').write_text(json.dumps(cases,indent=2));D.CR.generate(folder,cases,args.workers)
    verify(folder,cases)
