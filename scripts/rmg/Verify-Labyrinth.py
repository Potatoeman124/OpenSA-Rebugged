"""Labyrinth native terrain, parameter response, continuity and historical package checks."""
import argparse,copy,json,sys,hashlib
from collections import deque
from pathlib import Path
from importlib.util import spec_from_file_location,module_from_spec
sys.dont_write_bytecode=True
spec=spec_from_file_location('area',Path(__file__).with_name('Verify-StartingArea.py'));A=module_from_spec(spec);spec.loader.exec_module(A)
CR=A.S.D.CR;COMMON=A.COMMON;ROOT=A.ROOT
LEVELS=('small','medium','high','extreme','ultra');WIDTHS=('narrow','standard','wide');ROUTES=('few','standard','many')

def schedule():
    base=dict(schema_version=18,preset='balanced',layout_family='labyrinth',seed='825300756769842102',size='256,256',players=4,
        terrain_complexity='medium',water_amount='standard',gravel_moss_amount='standard',neutral_colony_density='standard',
        original_surface_relations=True,prevent_colony_overlapping=True,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,100),
        starting_colony_shares=[0]*4,passage_width='standard',extra_routes='standard')
    cases=[]
    def add(name,**kw):
        s=copy.deepcopy(base);s.update(kw)
        if len(s['starting_colony_shares'])!=s['players']:s['starting_colony_shares']=[0]*s['players']
        cases.append(dict(id=name,settings=s))
    for level in LEVELS:
        for width in WIDTHS:
            for routes in ROUTES:add(f'geometry-{level}-{width}-{routes}',terrain_complexity=level,passage_width=width,extra_routes=routes)
    for water in ('low','standard','high','extreme','ultra'):
        for surface in ('low','standard','high','extreme','ultra'):
            for original in (True,False):add(f'quantities-{water}-{surface}-{original}',terrain_complexity='ultra',water_amount=water,gravel_moss_amount=surface,original_surface_relations=original,passage_width='narrow')
    for size in (64,128,256,512):
        for players in range(1,(4 if size==64 else 8)+1):
            for extreme in (False,True):
                add(f'size-{size}-{players}-{extreme}',size=f'{size},{size}',players=players,seed=str((0,1,18446744073709551615)[players%3]),
                    terrain_complexity='ultra' if extreme else 'medium',water_amount='ultra' if extreme else 'standard',gravel_moss_amount='ultra' if extreme else 'standard',
                    neutral_colony_density='ultra' if extreme else 'standard',prevent_colony_overlapping=not extreme,respect_starting_safe_area=not extreme,
                    passage_width='narrow' if extreme else 'wide',extra_routes='few' if extreme else 'many',original_surface_relations=not extreme)
    for original in (True,False):
        for safe in (True,False):
            for strict in (True,False):
                for density in ('sparse','standard','dense','extreme','ultra'):
                    add(f'pressure-{original}-{safe}-{strict}-{density}',terrain_complexity='ultra',original_surface_relations=original,
                        respect_starting_safe_area=safe,prevent_colony_overlapping=strict,neutral_colony_density=density)
    for theme in ('DESERT','SWAMP','CANDY'):add('theme-'+theme,tileset=theme,terrain_complexity='ultra')
    for species in COMMON.KEYS:add('species-'+species,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0)|{species:100})
    add('empty',neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0))
    add('ownership-near',starting_colony_shares=[0,10,20,30])
    add('ownership-random',starting_colony_shares=[40,40,80,40],starting_colony_mode='random')
    for seed in (17,42,1337,20260910,748797295927410807,866069301331643517):
        for size,players in ((64,4),(128,8),(256,8)):
            add(f'seed-{seed}-{size}',seed=str(seed),size=f'{size},{size}',players=players,terrain_complexity='ultra',passage_width='narrow',extra_routes='few',water_amount='ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',prevent_colony_overlapping=False,respect_starting_safe_area=False)
    for seed in (42,20260910):add(f'seed-{seed}-512',seed=str(seed),size='512,512',players=8,terrain_complexity='ultra',passage_width='narrow',extra_routes='few',water_amount='ultra',neutral_colony_density='ultra')
    for size,players in ((64,4),(128,8),(512,8)):
        for li,level in enumerate(LEVELS):
            for wi,width in enumerate(WIDTHS):
                add(f'scale-{size}-{level}-{width}',size=f'{size},{size}',players=players,seed='642188072337235576',terrain_complexity=level,passage_width=width,extra_routes=ROUTES[(li+wi)%3],neutral_colony_density='ultra',prevent_colony_overlapping=False,original_surface_relations=li%2==0)
    for level in LEVELS:add('review-'+level,seed='642188072337235576',tileset='DESERT',players=6,terrain_complexity=level,passage_width='narrow',extra_routes='few')
    cases.extend(c for c in A.schedule() if 'baseline' in c)
    old=ROOT/'artifacts/rmg/starting-area/matrix-final'
    for name in ('forts-True-False-True','forts-False-True-True')+tuple(f'{f}-256-False-False' for f in A.FAMILIES):
        cases.append(dict(id='replay-area-'+name,settings=json.loads((old/name/'settings.json').read_text()),baseline=str(old/name/'map.oramap')))
    return cases

def verify(folder,cases):
    rows=[];terrains={};reports={};starts={};historical=0
    for c in cases:
        name=c['id'];p=folder/name
        if 'baseline' in c:
            assert COMMON.members(p/'map.oramap')==COMMON.members(c['baseline']),name
            historical+=1;continue
        s=c['settings'];r=json.loads((p/'report.json').read_text());g=r['regions'];v=r['native_validation'];plan=g['labyrinth_plan']
        assert r['generator_version']==23 and v['accepted'] and r['performance']['repeatability_checked'] and r['package_validation']['map_yaml_lint']=='passed',name
        assert all(v[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),name
        policy=v;assert policy['labyrinth_connected'] and policy['labyrinth_blocked_passage_cells']==0,name
        assert all(d>=2 for d in policy['labyrinth_shore_clearances_native']),name
        assert g['symmetry_requirement']=='NOT_REQUIRED' and not g['terrain_repainted_for_placement'],name
        assert g['neutral_colonies_placed']<=min(g['neutral_colonies_requested'],plan['colony_capacity_sites']),name
        actors=COMMON.actors(p/'map.oramap');starts[name]=[a for a in actors if a[0]=='mpspawn'];assert len(starts[name])==s['players'],name
        anchors={(x-2,y-2) for x,y in plan['chambers']}
        assert all((x,y) in anchors for t,x,y in actors if t.endswith('_colony')),name
        n=int(s['size'].split(',')[0])
        two,four={'sparse':(8,12),'standard':(10,16),'dense':(20,24),'extreme':(32,40),'ultra':(52,64)}[s['neutral_colony_density']]
        usual=two+(s['players']-2)*(four-two)//2
        usual=usual*9 if n==512 else usual*3 if n==256 else (usual+3)//4 if n==64 else usual
        assert g['neutral_colonies_density_target']==(usual+3)//4,name
        native=(p/'actual/semantic.u8').read_bytes();assert len(native)==n*n
        terrains[name]=native;reports[name]=g
        rows.append(dict(id=name,colonies=g['neutral_colonies_placed'],target=g['neutral_colonies_requested'],water=g['metrics']['water_percent_map'],gravel=g['metrics']['gravel_percent_land'],moss=g['metrics']['moss_percent_land'],max_dirt_square=g['metrics']['largest_all_dirt_square_native'],generation_ms=r['performance']['logical_generation_ms']))
    for width in WIDTHS:
        for routes in ROUTES:
            names=[f'geometry-{l}-{width}-{routes}' for l in LEVELS];plans=[reports[x]['labyrinth_plan'] for x in names]
            assert len({hashlib.sha256(terrains[x]).hexdigest() for x in names})==5
            assert all(starts[x]==starts[names[0]] for x in names)
            for field in ('macro_nodes','macro_tree'):assert all(p[field]==plans[0][field] for p in plans)
            assert all(plans[i]['maze_nodes']<plans[i+1]['maze_nodes'] for i in range(4))
            assert all(plans[i]['route_length_native']<plans[i+1]['route_length_native'] for i in range(4))
            assert plans[-1]['route_length_native']>plans[0]['route_length_native']*2
            assert all(plans[i]['passage_width_native']>plans[i+1]['passage_width_native'] for i in range(4))
            # Independent native shoreline density: real barriers, not merely extra
            # graph vertices on an otherwise unchanged terrain raster.
            def boundary(data):
                n=256
                return sum((data[y*n+x]==1)!=(data[y*n+x+1]==1) for y in range(n) for x in range(n-1))+sum((data[y*n+x]==1)!=(data[(y+1)*n+x]==1) for y in range(n-1) for x in range(n))
            assert boundary(terrains[names[-1]])>boundary(terrains[names[0]])*1.5,names
    for original in (True,False):
        names=[x for x in terrains if x.startswith(f'pressure-{original}-')]
        assert len({hashlib.sha256(terrains[x]).hexdigest() for x in names})==1,'Colonies or safety repainted terrain'
    names=['geometry-medium-standard-standard','empty','ownership-near','ownership-random']+['species-'+s for s in COMMON.KEYS]
    assert len({hashlib.sha256(terrains[x]).hexdigest() for x in names})==1,'Weights or ownership changed terrain'
    for l in LEVELS:
        plans=[reports[f'geometry-{l}-standard-{route}']['labyrinth_plan'] for route in ROUTES]
        assert plans[0]['extra_edges']<plans[1]['extra_edges']<plans[2]['extra_edges']
        assert all(p['macro_tree']==plans[0]['macro_tree'] and p['macro_nodes']==plans[0]['macro_nodes'] for p in plans)
    for original in (True,False):
        for surface in ('low','standard','high','extreme','ultra'):
            names=[f'quantities-{w}-{surface}-{original}' for w in ('low','standard','high','extreme','ultra')]
            amounts=[reports[n]['metrics']['water_percent_map'] for n in names]
            assert all(amounts[i+1]>amounts[i]+.5 for i in range(4)),(names,amounts)
    review=[]
    for level in LEVELS:
        name='review-'+level;data=terrains[name];positions=[(x+2,y+2) for t,x,y in starts[name]];pair_lengths=[];n=256
        for k,(x,y) in enumerate(positions[:-1]):
            d=[-1]*len(data);d[y*n+x]=0;q=deque([y*n+x])
            while q:
                at=q.popleft();px=at%n;py=at//n
                for adjacent in ((at-1 if px else -1),(at+1 if px<n-1 else -1),(at-n if py else -1),(at+n if py<n-1 else -1)):
                    if adjacent>=0 and d[adjacent]<0 and data[adjacent]!=1:d[adjacent]=d[at]+1;q.append(adjacent)
            pair_lengths.extend(d[y*n+x] for x,y in positions[k+1:])
        assert min(pair_lengths)>0,name
        plan=reports[name]['labyrinth_plan'];review.append(dict(complexity=level,junctions=plan['maze_nodes'],network_length=plan['route_length_native'],protected_width=plan['passage_width_native'],native_mean_walk=sum(pair_lengths)/len(pair_lengths)))
        assert starts[name]==starts['review-small']
    assert review[-1]['network_length']>review[0]['network_length']*3
    assert review[-1]['native_mean_walk']>review[0]['native_mean_walk']*1.1
    output=dict(status='PASS',generated_cases=len(rows),historical_packages=historical,review_seed=review,cases=rows)
    (folder/'verification.json').write_text(json.dumps(output,indent=2));print(json.dumps({k:v for k,v in output.items() if k!='cases'}),flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output',required=True);p.add_argument('--workers',type=int,default=4);p.add_argument('--verify-only',action='store_true');args=p.parse_args()
    folder=Path(args.output).resolve();folder.mkdir(parents=True,exist_ok=True);cases=schedule()
    if not args.verify_only:CR.generate(folder,cases,args.workers)
    verify(folder,cases)
