"""Chaos: native collision terrain, mixed biomes, local flying access and historical replay."""
import argparse,copy,json,re,struct,sys
from pathlib import Path
from importlib.util import spec_from_file_location,module_from_spec
sys.dont_write_bytecode=True
spec=spec_from_file_location('arch',Path(__file__).with_name('Verify-Archipelago.py'));A=module_from_spec(spec);spec.loader.exec_module(A)
CR=A.CR;COMMON=A.COMMON;ROOT=A.ROOT
LEVELS=('small','medium','high','extreme','ultra');SCALES=('small','standard','large');BIOMES=('single','patchwork','fractured');QUANTITIES=('low','standard','high','extreme','ultra')

def schedule():
    base=dict(schema_version=20,preset='balanced',layout_family='chaos',seed='642188072337235576',size='256,256',players=4,
        terrain_complexity='medium',water_amount='standard',gravel_moss_amount='standard',neutral_colony_density='standard',
        chaos_scale='standard',chaos_biomes='patchwork',original_surface_relations=True,prevent_colony_overlapping=True,
        neutral_colony_weights=dict.fromkeys(COMMON.KEYS,100),starting_colony_shares=[0]*4)
    cases=[]
    def add(name,**kw):
        s=copy.deepcopy(base);s.update(kw)
        if len(s['starting_colony_shares'])!=s['players']:s['starting_colony_shares']=[0]*s['players']
        cases.append(dict(id=name,settings=s))
    for scale in SCALES:
        for biome in BIOMES:
            for level in LEVELS:add(f'geometry-{scale}-{biome}-{level}',chaos_scale=scale,chaos_biomes=biome,terrain_complexity=level)
    for level in LEVELS:add('review-'+level,terrain_complexity=level,chaos_biomes='fractured',water_amount='high',gravel_moss_amount='ultra',original_surface_relations=False)
    for water in QUANTITIES:
        for surface in QUANTITIES:
            for original in (True,False):add(f'quantities-{water}-{surface}-{original}',terrain_complexity='ultra',water_amount=water,gravel_moss_amount=surface,original_surface_relations=original)
    for size in (64,128,256,512):
        for players in range(1,(4 if size==64 else 8)+1):
            for extreme in (False,True):
                add(f'size-{size}-{players}-{extreme}',size=f'{size},{size}',players=players,seed=str((0,1,18446744073709551615)[players%3]),
                    terrain_complexity='ultra' if extreme else 'medium',water_amount='ultra' if extreme else 'standard',gravel_moss_amount='ultra' if extreme else 'standard',
                    chaos_scale=SCALES[players%3],chaos_biomes='fractured' if extreme else 'patchwork',neutral_colony_density='ultra' if extreme else 'standard',
                    prevent_colony_overlapping=not extreme,respect_starting_safe_area=not extreme,original_surface_relations=not extreme)
    for original in (True,False):
        for safe in (True,False):
            for strict in (True,False):
                for density in ('sparse','standard','dense','extreme','ultra'):add(f'pressure-{original}-{safe}-{strict}-{density}',original_surface_relations=original,respect_starting_safe_area=safe,prevent_colony_overlapping=strict,neutral_colony_density=density,terrain_complexity='ultra')
    for theme in ('NORMAL','DESERT','SWAMP','CANDY'):
        for biome in BIOMES:add(f'theme-{theme}-{biome}',tileset=theme,chaos_biomes=biome,terrain_complexity='ultra')
    for species in COMMON.KEYS:add('species-'+species,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0)|{species:100})
    for biome in BIOMES:add('empty-'+biome,chaos_biomes=biome,neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0),starting_colony_shares=[100]*4)
    add('ownership-near',starting_colony_shares=[0,10,20,30])
    add('ownership-random',starting_colony_shares=[40,40,80,40],starting_colony_mode='random')
    add('ownership-all',starting_colony_shares=[100]*4)
    for seed in (17,42,1337,20260911,748797295927410807,866069301331643517):
        for size,players in ((64,4),(128,8),(256,8)):
            for scale in SCALES:
                add(f'seed-{seed}-{size}-{scale}',seed=str(seed),size=f'{size},{size}',players=players,chaos_scale=scale,terrain_complexity='ultra',water_amount='ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',chaos_biomes='fractured',prevent_colony_overlapping=False,respect_starting_safe_area=False,original_surface_relations=False)
        add(f'seed-{seed}-512',seed=str(seed),size='512,512',players=8,terrain_complexity='ultra',water_amount='ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',chaos_scale='large',chaos_biomes='fractured',prevent_colony_overlapping=False,respect_starting_safe_area=False,original_surface_relations=False)
    # Both controls across the smallest and largest canvases, including all source biomes.
    for size,players in ((64,4),(128,8),(512,8)):
        for scale in SCALES:
            for bi in BIOMES:
                add(f'canvas-{size}-{scale}-{bi}',size=f'{size},{size}',players=players,chaos_scale=scale,chaos_biomes=bi,tileset=('DESERT','SWAMP','CANDY')[SCALES.index(scale)],terrain_complexity='ultra',neutral_colony_density='ultra',prevent_colony_overlapping=False)
    cases.extend(c for c in A.schedule() if 'baseline' in c)
    old=ROOT/'artifacts/rmg/archipelago/matrix-02'
    for name in ('review-small','review-ultra','geometry-few-ultra-medium','geometry-ultra-small-ultra','scale-64-4-True','scale-512-8-True','empty','ownership-random'):
        cases.append(dict(id='replay-archipelago-'+name,settings=json.loads((old/name/'settings.json').read_text()),baseline=str(old/name/'map.oramap')))
    return cases

def native_biomes(binary,n):
    version,w,h=struct.unpack_from('<BHH',binary)
    offset=5 if version==1 else struct.unpack_from('<I',binary,5)[0]
    assert w==h==n+4,(w,h,n)
    bands=[];types=[]
    for y in range(2,n+2):
        for x in range(2,n+2):
            tile,frame=struct.unpack_from('<HB',binary,offset+3*(x*h+y))
            assert tile%256<=100 and tile//256<4 and frame<4,(tile,frame)
            types.append(tile%256);bands.append(tile//256)
    return bytes(bands),types

def border_count(data,n):
    return sum(data[i]!=data[i+1] for i in range(len(data)-1) if i%n<n-1)+sum(data[i]!=data[i+n] for i in range(len(data)-n))

def verify(folder,cases):
    rows=[];maps={};starts={};bands={};plans={};replays=0
    for c in cases:
        name=c['id'];p=folder/name
        if 'baseline' in c:
            assert COMMON.members(p/'map.oramap')==COMMON.members(c['baseline']),name;replays+=1;continue
        r=json.loads((p/'report.json').read_text());g=r['regions'];v=r['native_validation'];s=c['settings'];plan=g['chaos_plan']
        assert r['generator_version']==25 and v['accepted'] and r['performance']['repeatability_checked'] and r['package_validation']['map_yaml_lint']=='passed',name
        assert all(v[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells','native_semantic_mismatches')),name
        assert v['chaos_nest_access'] and min(v['chaos_shore_clearances_native'])>=2,name
        native=(p/'actual/semantic.u8').read_bytes();n=int(s['size'].split(',')[0]);labels,areas=A.components(native,n)
        assert len(areas)==plan['islands_actual']==v['chaos_islands']==v['chaos_mandatory_neutral_nests'],(name,areas)
        assert sorted(areas)==sorted(v['chaos_island_areas']),name
        nests=v['chaos_mandatory_nests'];actors=COMMON.actors(p/'map.oramap');colonies=[a for a in actors if a[0].endswith('_colony')]
        assert len({labels[y*n+x] for x,y in nests})==len(areas) and all(('wasps_colony',x,y) in colonies for x,y in nests),name
        members=COMMON.members(p/'map.oramap');yaml=members['map.yaml'].decode();rules=members['rules.yaml'].decode() if 'rules.yaml' in members else yaml
        actor_names={}
        for match in re.finditer(r'\t(Actor\d+): (\w+)\n(.*?)(?=\n\tActor\d+:|\n[^\t]|\Z)',yaml,re.S):
            location=re.search(r'Location: (\d+),(\d+)',match[3])
            if location:actor_names[(match[2],int(location[1])-2,int(location[2])-2)]=match[1]
        pool=re.search(r'ColonyActorNames: ([^\n]*)',rules);ownership=set(pool[1].split(', ')) if pool else set()
        assert all(actor_names[('wasps_colony',x,y)] not in ownership for x,y in nests),name
        assert len(ownership)==len(colonies)-len(nests),name
        shares=s['starting_colony_shares'];total=sum(shares);quota=(len(ownership)*min(total,100)+99)//100
        counts=[quota*v//total if total else 0 for v in shares]
        if total:
            for i in sorted(range(len(shares)),key=lambda i:(-(quota*shares[i]%total),i))[:quota-sum(counts)]:counts[i]+=1
        assert g['starting_colonies_allocated_if_all_slots_occupied']==counts,name
        for t,x,y in colonies:assert (t=='wasps_colony' and [x,y] in nests) or s['neutral_colony_weights'][t.removesuffix('_colony')]>0,name
        assert not g['terrain_repainted_for_placement'] and g['symmetry_requirement']=='NOT_REQUIRED',name
        assert len(colonies)<=max(g['neutral_colonies_requested'],len(nests)),name
        maps[name]=native;starts[name]=[a for a in actors if a[0]=='mpspawn'];assert len(starts[name])==s['players'],name
        actual_bands,_=native_biomes(members['map.bin'],n);bands[name]=actual_bands
        mixed=s['chaos_biomes']!='single';actual_tileset=re.search(r'^Tileset: ([^\n]+)',yaml,re.M)[1]
        assert actual_tileset==('CHAOS' if mixed else s.get('tileset','NORMAL')),name
        assert set(actual_bands)==({0,1,2,3} if mixed else {0}),name
        assert [actual_bands.count(i) for i in range(4)]==v['chaos_biome_cells'],name
        # The map must not accidentally fall back to a reflected competitive layout.
        horizontal=sum(native[y*n+x]!=native[y*n+n-1-x] for y in range(n) for x in range(n))
        vertical=sum(native[y*n+x]!=native[(n-1-y)*n+x] for y in range(n) for x in range(n))
        assert min(horizontal,vertical)>n*n*.01,(name,horizontal,vertical)
        plans[name]=plan
        rows.append(dict(id=name,islands=len(areas),largest_island=max(areas),land=sum(areas),shoreline=A.boundary(native,n),surface_boundaries=border_count(native,n),biome_boundaries=border_count(actual_bands,n),biome_cells=v['chaos_biome_cells'],collisions=plan['collision_count'],colonies=len(colonies),mandatory_nests=len(nests),water=g['metrics']['water_percent_map'],gravel=g['metrics']['gravel_percent_land'],moss=g['metrics']['moss_percent_land'],generation_ms=r['performance']['logical_generation_ms']))
    by={r['id']:r for r in rows}
    def existing(names):return all(name in maps for name in names)
    def same_terrain(names,message):
        if existing(names):assert len({maps[name] for name in names})==1,message
    for scale in SCALES:
        for level in LEVELS:
            names=[f'geometry-{scale}-{biome}-{level}' for biome in BIOMES]
            same_terrain(names,'Biome mode changed geometry')
            if existing(names):assert by[names[2]]['biome_boundaries']>by[names[1]]['biome_boundaries']*1.5,'Fractured biome changes are too weak'
    for biome in BIOMES:
        names=[f'theme-{theme}-{biome}' for theme in ('NORMAL','DESERT','SWAMP','CANDY')]
        same_terrain(names,'Base biome changed native geometry')
    for original in (True,False):
        names=[name for name in maps if name.startswith(f'pressure-{original}-')]
        if names:assert len({maps[name] for name in names})==1,'Colony pressure changed geography'
    same_terrain(['geometry-standard-patchwork-medium','empty-patchwork','ownership-near','ownership-random','ownership-all']+['species-'+x for x in COMMON.KEYS],'Weights or ownership changed terrain')
    for scale in SCALES:
        names=[f'geometry-{scale}-single-{level}' for level in LEVELS]
        if existing(names):
            assert all(starts[name]==starts[names[0]] for name in names),'Complexity moved starts'
            counts=[by[name]['collisions'] for name in names];assert all(a<b for a,b in zip(counts,counts[1:])),counts
            assert sum(a!=b for a,b in zip(maps[names[0]],maps[names[-1]]))>256*256*.05,'Complexity changes too little actual terrain'
    for level in LEVELS:
        names=[f'geometry-{scale}-single-{level}' for scale in SCALES]
        if existing(names):
            assert all(starts[name]==starts[names[0]] for name in names),'Collision scale moved starts'
            assert sum(a!=b for a,b in zip(maps[names[0]],maps[names[-1]]))>256*256*.05,'Collision scale changes too little actual terrain'
    for original in (True,False):
        for surface in QUANTITIES:
            names=[f'quantities-{water}-{surface}-{original}' for water in QUANTITIES]
            if existing(names):
                values=[by[name]['water'] for name in names]
                assert values[-1]>values[0]+12,(original,surface,values)
                assert all(starts[name]==starts[names[0]] for name in names),'Water amount moved starts'
    for name in maps:
        if name.startswith('empty-'):assert by[name]['colonies']==by[name]['mandatory_nests'],name
    review=[by['review-'+level] for level in LEVELS if 'review-'+level in by]
    output=dict(status='PASS',generated_cases=len(rows),historical_packages=replays,review=review,cases=rows)
    (folder/'verification.json').write_text(json.dumps(output,indent=2));print(json.dumps({k:v for k,v in output.items() if k!='cases'},indent=2))

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--output',required=True);ap.add_argument('--verify-only',action='store_true');ap.add_argument('--workers',type=int,default=4);ap.add_argument('--filter',help='Regular expression selecting case IDs');args=ap.parse_args()
    folder=Path(args.output).resolve();folder.mkdir(parents=True,exist_ok=True);cases=schedule()
    if args.filter:cases=[case for case in cases if re.search(args.filter,case['id'])]
    if not args.verify_only:(folder/'schedule.json').write_text(json.dumps(cases,indent=2));CR.generate(folder,cases,args.workers)
    verify(folder,cases)
