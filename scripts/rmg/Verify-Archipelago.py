"""Archipelago: actual islands, mandatory neutral nests, controls and historical replays."""
import argparse,copy,json,sys,hashlib,re
from pathlib import Path
from collections import deque
from importlib.util import spec_from_file_location,module_from_spec
sys.dont_write_bytecode=True
spec=spec_from_file_location('lab',Path(__file__).with_name('Verify-Labyrinth.py'));L=module_from_spec(spec);spec.loader.exec_module(L)
CR=L.CR;COMMON=L.COMMON;ROOT=L.ROOT
LEVELS=('small','medium','high','extreme','ultra');AMOUNTS=('few','standard','many','extreme','ultra');SIZES=('small','standard','large','extreme','ultra');QUANTITIES=('low','standard','high','extreme','ultra')

def schedule():
    base=dict(schema_version=19,preset='balanced',layout_family='archipelago',seed='642188072337235576',size='256,256',players=4,
        terrain_complexity='medium',water_amount='standard',gravel_moss_amount='standard',neutral_colony_density='standard',
        island_amount='standard',island_size='standard',original_surface_relations=True,prevent_colony_overlapping=True,
        neutral_colony_weights=dict.fromkeys(COMMON.KEYS,100),starting_colony_shares=[0]*4)
    cases=[]
    def add(name,**kw):
        s=copy.deepcopy(base);s.update(kw)
        if len(s['starting_colony_shares'])!=s['players']:s['starting_colony_shares']=[0]*s['players']
        cases.append(dict(id=name,settings=s))
    for amount in AMOUNTS:
        for size in SIZES:
            for level in ('small','medium','ultra'):add(f'geometry-{amount}-{size}-{level}',island_amount=amount,island_size=size,terrain_complexity=level)
    for level in LEVELS:add('review-'+level,terrain_complexity=level,island_amount='few',island_size='large')
    for water in QUANTITIES:
        for surface in QUANTITIES:
            for original in (True,False):add(f'quantities-{water}-{surface}-{original}',terrain_complexity='ultra',water_amount=water,gravel_moss_amount=surface,original_surface_relations=original)
    for size in (64,128,256,512):
        for players in range(1,(4 if size==64 else 8)+1):
            for extreme in (False,True):
                add(f'scale-{size}-{players}-{extreme}',size=f'{size},{size}',players=players,seed=str((0,1,18446744073709551615)[players%3]),
                    terrain_complexity='ultra' if extreme else 'medium',water_amount='ultra' if extreme else 'standard',gravel_moss_amount='ultra' if extreme else 'standard',
                    island_amount='ultra' if extreme else 'few',island_size='small' if extreme else 'ultra',neutral_colony_density='ultra' if extreme else 'standard',
                    prevent_colony_overlapping=not extreme,respect_starting_safe_area=not extreme,original_surface_relations=not extreme)
    for original in (True,False):
        for safe in (True,False):
            for strict in (True,False):
                for density in ('sparse','standard','dense','extreme','ultra'):add(f'pressure-{original}-{safe}-{strict}-{density}',original_surface_relations=original,respect_starting_safe_area=safe,prevent_colony_overlapping=strict,neutral_colony_density=density,terrain_complexity='ultra')
    for theme in ('DESERT','SWAMP','CANDY'):add('theme-'+theme,tileset=theme,terrain_complexity='ultra')
    for species in COMMON.KEYS:add('species-'+species,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0)|{species:100})
    add('empty',neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0),starting_colony_shares=[100]*4)
    add('ownership-near',starting_colony_shares=[0,10,20,30])
    add('ownership-random',starting_colony_shares=[40,40,80,40],starting_colony_mode='random')
    for seed in (17,42,1337,20260911,748797295927410807,866069301331643517):
        for size,players in ((64,4),(128,8),(256,8)):
            for amount in ('few','standard','ultra'):
                add(f'seed-{seed}-{size}-{amount}',seed=str(seed),size=f'{size},{size}',players=players,island_amount=amount,terrain_complexity='ultra',water_amount='ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',island_size='small',prevent_colony_overlapping=False)
    cases.extend(c for c in L.schedule() if 'baseline' in c)
    old=ROOT/'artifacts/rmg/labyrinth/revision/matrix-01'
    for name in ('review-small','review-ultra','size-64-4-True','size-512-8-True'):
        cases.append(dict(id='replay-labyrinth-'+name,settings=json.loads((old/name/'settings.json').read_text()),baseline=str(old/name/'map.oramap')))
    return cases

def components(data,n):
    labels=[-1]*len(data);sizes=[]
    for i,v in enumerate(data):
        if v==1 or labels[i]>=0:continue
        label=len(sizes);count=0;q=deque([i]);labels[i]=label
        while q:
            at=q.popleft();count+=1;x=at%n;y=at//n
            for to in (at-1 if x else -1,at+1 if x<n-1 else -1,at-n if y else -1,at+n if y<n-1 else -1):
                if to>=0 and data[to]!=1 and labels[to]<0:labels[to]=label;q.append(to)
        sizes.append(count)
    return labels,sizes

def boundary(data,n):
    return sum((data[i]==1)!=(data[i+1]==1) for i in range(len(data)-1) if i%n<n-1)+sum((data[i]==1)!=(data[i+n]==1) for i in range(len(data)-n))

def verify(folder,cases):
    rows=[];maps={};starts={};replays=0
    for c in cases:
        name=c['id'];p=folder/name
        if 'baseline' in c:
            assert COMMON.members(p/'map.oramap')==COMMON.members(c['baseline']),name;replays+=1;continue
        r=json.loads((p/'report.json').read_text());g=r['regions'];v=r['native_validation'];s=c['settings'];plan=g['archipelago_plan']
        assert r['generator_version']==24 and v['accepted'] and r['performance']['repeatability_checked'] and r['package_validation']['map_yaml_lint']=='passed',name
        assert all(v[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),name
        assert v['archipelago_nest_access'] and min(v['archipelago_shore_clearances_native'])>=2,name
        native=(p/'actual/semantic.u8').read_bytes();n=int(s['size'].split(',')[0]);labels,areas=components(native,n)
        assert len(areas)==plan['islands_requested']==plan['islands_actual']==v['archipelago_mandatory_neutral_nests'],(name,areas)
        nests=plan['mandatory_nests'];actors=COMMON.actors(p/'map.oramap');colonies=[a for a in actors if a[0].endswith('_colony')]
        assert len({labels[y*n+x] for x,y in nests})==len(areas) and all(('wasps_colony',x,y) in colonies for x,y in nests),name
        # Mandatory nests are actual map actors omitted from the runtime ownership pool.
        members=COMMON.members(p/'map.oramap');yaml=members['map.yaml'].decode();rules=members['rules.yaml'].decode() if 'rules.yaml' in members else yaml
        # Actor child order is engine-defined; get anchors without assuming Location first.
        actor_names={}
        for match in re.finditer(r'\t(Actor\d+): (\w+)\n(.*?)(?=\n\tActor\d+:|\n[^\t]|\Z)',yaml,re.S):
            location=re.search(r'Location: (\d+),(\d+)',match[3])
            if location:actor_names[(match[2],int(location[1])-2,int(location[2])-2)]=match[1]
        pool=re.search(r'ColonyActorNames: ([^\n]*)',rules)
        ownership=set(pool[1].split(', ')) if pool else set()
        assert all(actor_names[('wasps_colony',x,y)] not in ownership for x,y in nests),name
        assert len(ownership)==len(colonies)-len(nests),name
        shares=s['starting_colony_shares'];total=sum(shares);quota=(len(ownership)*min(total,100)+99)//100
        counts=[quota*v//total if total else 0 for v in shares]
        if total:
            for i in sorted(range(len(shares)),key=lambda i:(-(quota*shares[i]%total),i))[:quota-sum(counts)]:counts[i]+=1
        assert g['starting_colonies_allocated_if_all_slots_occupied']==counts,name
        for t,x,y in colonies:
            assert (t=='wasps_colony' and [x,y] in nests) or s['neutral_colony_weights'][t.removesuffix('_colony')]>0,name
        assert not g['terrain_repainted_for_placement'] and g['symmetry_requirement']=='NOT_REQUIRED',name
        assert len(colonies)<=max(g['neutral_colonies_requested'],len(nests)),name
        maps[name]=native;starts[name]=[a for a in actors if a[0]=='mpspawn'];assert len(starts[name])==s['players'],name
        rows.append(dict(id=name,islands=len(areas),largest_island=max(areas),land=sum(areas),shoreline=boundary(native,n),colonies=len(colonies),mandatory_nests=len(nests),water=g['metrics']['water_percent_map'],generation_ms=r['performance']['logical_generation_ms']))
    by={r['id']:r for r in rows}
    for amount in AMOUNTS:
        names=[f'geometry-{amount}-{size}-{level}' for size in SIZES for level in ('small','medium','ultra')]
        assert all(starts[name]==starts[names[0]] for name in names),'Island size or complexity moved starts'
    for theme in ('DESERT','SWAMP','CANDY'):
        assert maps['theme-'+theme]==maps['geometry-standard-standard-ultra'],'Biome changed native geometry'
    quantity_names=[name for name in maps if name.startswith('quantities-')]
    assert all(starts[name]==starts[quantity_names[0]] for name in quantity_names),'Water or surfaces moved starts'
    for amount in AMOUNTS:
        for level in ('small','medium','ultra'):
            values=[by[f'geometry-{amount}-{size}-{level}']['land'] for size in SIZES]
            assert all(a<b for a,b in zip(values,values[1:])),(amount,level,values)
    for size in SIZES:
        values=[by[f'geometry-{a}-{size}-medium']['islands'] for a in AMOUNTS];assert all(a<b for a,b in zip(values,values[1:])),values
    for original in (True,False):
        names=[name for name in maps if name.startswith(f'pressure-{original}-')]
        assert len({maps[name] for name in names})==1,'Colony pressure changed geography'
    names=['geometry-standard-standard-medium','empty','ownership-near','ownership-random']+['species-'+x for x in COMMON.KEYS]
    assert len({maps[name] for name in names})==1,'Weights or ownership changed terrain'
    for original in (True,False):
        for surface in QUANTITIES:
            values=[by[f'quantities-{w}-{surface}-{original}']['water'] for w in QUANTITIES]
            assert all(b>a+.5 for a,b in zip(values,values[1:])),values
    review=[by['review-'+l] for l in LEVELS]
    assert all(starts['review-'+l]==starts['review-small'] for l in LEVELS)
    assert review[-1]['shoreline']>review[0]['shoreline']*1.3,review
    assert by['geometry-few-ultra-medium']['largest_island']>256*256*.25,'No substantial landmass'
    assert by['empty']['colonies']==by['empty']['mandatory_nests']
    output=dict(status='PASS',generated_cases=len(rows),historical_packages=replays,review=review,cases=rows)
    (folder/'verification.json').write_text(json.dumps(output,indent=2));print(json.dumps({k:v for k,v in output.items() if k!='cases'},indent=2))

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--output',required=True);ap.add_argument('--verify-only',action='store_true');ap.add_argument('--workers',type=int,default=4);args=ap.parse_args()
    folder=Path(args.output).resolve();folder.mkdir(parents=True,exist_ok=True);cases=schedule()
    if not args.verify_only:(folder/'schedule.json').write_text(json.dumps(cases,indent=2));CR.generate(folder,cases,args.workers)
    verify(folder,cases)
