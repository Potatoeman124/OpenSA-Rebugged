"""Ring native parameter matrix, route topology and accepted-layout replays."""
import argparse,copy,hashlib,json,math,sys
from pathlib import Path
from importlib.util import module_from_spec,spec_from_file_location
sys.dont_write_bytecode=True
SPEC=spec_from_file_location('crossroads',Path(__file__).with_name('Verify-Crossroads.py'))
CR=module_from_spec(SPEC);SPEC.loader.exec_module(CR)
BF=CR.BF;COMMON=CR.COMMON;ROOT=CR.ROOT;LEVELS=CR.LEVELS
SHAPES=('round','octagonal','square');WIDTHS=('narrow','standard','wide')

def schedule(wide=False):
    base=dict(schema_version=14,preset='balanced',layout_family='ring',seed='397716241463670640',size='256,256',players=4,
        terrain_complexity='medium',ring_shape='round',ring_width='standard',water_amount='standard',gravel_moss_amount='standard',
        neutral_colony_density='standard',original_surface_relations=True,prevent_colony_overlapping=True,
        neutral_colony_weights=dict.fromkeys(COMMON.KEYS,100),starting_colony_shares=[0,10,20,30])
    cases=[]
    def add(name,**changes):
        s=copy.deepcopy(base);s.update(changes)
        if len(s['starting_colony_shares'])!=s['players']:s['starting_colony_shares']=[0]*s['players']
        cases.append(dict(id=name,settings=s))
    for shape in SHAPES:
        for level in LEVELS:add(shape+'-'+level,ring_shape=shape,terrain_complexity=level)
        for width in WIDTHS:add(shape+'-'+width,ring_shape=shape,ring_width=width)
    for field,levels in (('water_amount',('low','high','extreme','ultra')),('gravel_moss_amount',('low','high','extreme','ultra')),('neutral_colony_density',('sparse','dense','extreme','ultra'))):
        for value in levels:add(field+'-'+value,**{field:value})
    for size in (64,128,256,512):
        for players in ((2,4) if size==64 else (2,4,8)):
            add(f'{size}-p{players}',size=f'{size},{size}',players=players,seed='0')
    for theme in ('DESERT','SWAMP','CANDY'):add('theme-'+theme,tileset=theme)
    add('two-horizontal',players=2,seed='1')
    add('random-ownership',starting_colony_mode='random')
    add('wasps-only',neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0)|dict(wasps=100))
    add('no-colonies',neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0))
    for strict in (True,False):add('512-ultra-'+str(strict),size='512,512',players=8,terrain_complexity='ultra',water_amount='ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',prevent_colony_overlapping=strict,original_surface_relations=strict)
    if wide:
        # Every supported size/player/shape/width/complexity combination, under colony pressure.
        seeds=('0','1','18446744073709551615')
        for size in (64,128,256,512):
            for players in ((2,4) if size==64 else (2,4,8)):
                for si,shape in enumerate(SHAPES):
                    for wi,width in enumerate(WIDTHS):
                        for ci,level in enumerate(LEVELS):
                            add(f'geometry-{size}-{players}-{shape}-{width}-{level}',size=f'{size},{size}',players=players,ring_shape=shape,ring_width=width,terrain_complexity=level,
                                seed=seeds[(si+wi+ci)%3],water_amount='low' if ci%2==0 else 'ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',prevent_colony_overlapping=False)
        # Pure same-seed control comparisons, including the visual saturation case at Ultra water.
        for shape in SHAPES:
            for width in WIDTHS:
                for water in ('standard','ultra'):
                    for level in LEVELS:
                        add(f'response-{shape}-{width}-{water}-{level}',ring_shape=shape,ring_width=width,terrain_complexity=level,
                            water_amount=water,gravel_moss_amount='ultra',neutral_colony_density='ultra',prevent_colony_overlapping=False,original_surface_relations=False)
        for shape in SHAPES:
            for water in ('low','standard','high','extreme','ultra'):
                for surface in ('low','standard','high','extreme','ultra'):
                    add(f'quantities-{shape}-{water}-{surface}',ring_shape=shape,terrain_complexity='ultra',water_amount=water,gravel_moss_amount=surface,
                        neutral_colony_density='ultra',prevent_colony_overlapping=False,original_surface_relations=False)
            for original in (True,False):
                for width in WIDTHS:
                    for density in ('sparse','standard','dense','extreme','ultra'):
                        add(f'density-{shape}-{width}-{density}-{original}',ring_shape=shape,ring_width=width,terrain_complexity='ultra',water_amount='ultra',gravel_moss_amount='ultra',
                            neutral_colony_density=density,prevent_colony_overlapping=False,original_surface_relations=original)
    cases.extend(c for c in CR.schedule() if 'baseline' in c)
    old=ROOT/'artifacts/rmg/crossroads/revision/matrix-final'
    for name in ('p4-medium','p8-ultra','routes-narrow-none','routes-wide-many','64-p4','128-eight-ultra','512-ultra-False','review-fourth-screenshot'):
        cases.append(dict(id='replay-crossroads-'+name,settings=json.loads((old/name/'settings.json').read_text()),baseline=str(old/name/'map.oramap')))
    return cases

def radius(x,y,shape):
    x,y=abs(x),abs(y)
    return math.hypot(x,y) if shape=='round' else max(x,y,(x+y)/math.sqrt(2)) if shape=='octagonal' else max(x,y)


def verify(folder,cases):
    records=[];maps={};objectives={};density_groups={};continuity_groups={}
    for case in cases:
        name=case['id'];out=folder/name;s=case['settings']
        if 'baseline' in case:
            assert COMMON.members(out/'map.oramap')==COMMON.members(case['baseline']),name
            continue
        r=json.loads((out/'report.json').read_text());v=r['native_validation'];g=r['regions'];plan=g['ring_plan']
        n=int(s['size'].split(',')[0]);axes={2:1,4:2,8:4}[s['players']];seed=int(s['seed'])
        assert v['accepted'] and r['performance']['repeatability_checked'] and r['package_validation']['map_yaml_lint']=='passed',name
        assert v['battlefield_ground_access'] and v['battlefield_weighted_pool_parity'] and v['ring_ground_loop_after_actors'],name
        assert all(v[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),name
        native=(out/'actual/semantic.u8').read_bytes();actors=COMMON.actors(out/'map.oramap')
        assert hashlib.sha256(native).hexdigest()==g['semantic_sha256'],name
        rows=[native[y*n:(y+1)*n] for y in range(n)]
        if axes>=2 or seed%2==0:assert rows==[r[::-1] for r in rows],name
        if axes>=2 or seed%2:assert rows==rows[::-1],name
        if axes==4:assert native==bytes(native[x*n+y] for y in range(n) for x in range(n)),name
        for kind in {t for t,x,y in actors}:
            points={(x,y) for t,x,y in actors if t==kind}
            assert all(COMMON.orbit(x,y,n,axes,seed)<=points for x,y in points),(name,kind)
        targets=[a for a in actors if a[0]=='mpspawn' or a[0].endswith('_colony')]
        colonies=[a for a in targets if a[0].endswith('_colony')]
        assert len(targets)-len(colonies)==s['players'] and len(colonies)%s['players']==0,name
        assert all(s['neutral_colony_weights'][t.removesuffix('_colony')]>0 for t,x,y in colonies),name
        center=(n-1)/2;rr=plan['ring_radius_native']
        assert native[(n//2)*n+n//2]==1 and plan['topology']['enclosed_central_lake'],name
        # Inspect the exported native loop directly, independently of the generator's masks.
        for i,value in enumerate(native):
            if abs(radius(i%n-center,i//n-center,s['ring_shape'])-rr)<=1:assert value==0,(name,'loop cell',i)
        for t,x,y in colonies:
            assert abs(radius(x-center,y-center,s['ring_shape'])-rr)<=plan['colony_belt_half_width_native'],(name,'colony outside belt')
        maps[name]=native;objectives[name]=targets;m=g['metrics']
        records.append(dict(id=name,placed=len(colonies),target=g['neutral_colonies_requested'],generation_ms=r['performance']['logical_generation_ms'],
            water=m['water_percent_map'],surfaces=m['gravel_percent_land']+m['moss_percent_land'],lake_cells=plan['topology']['central_lake_cells']))
        options=copy.deepcopy(s)
        for key in ('neutral_colony_density','prevent_colony_overlapping','neutral_colony_weights','starting_colony_shares','starting_colony_mode'):
            options.pop(key,None)
        density_groups.setdefault(json.dumps(options,sort_keys=True),[]).append(name)
        options=copy.deepcopy(s)
        for key in ('terrain_complexity','water_amount','gravel_moss_amount','ring_width','original_surface_relations','starting_colony_shares','starting_colony_mode','tileset'):
            options.pop(key,None)
        continuity_groups.setdefault(json.dumps(options,sort_keys=True),[]).append(name)
    density_comparisons=0;continuity_comparisons=0
    for names in density_groups.values():
        for name in names[1:]:
            assert bytes(v==1 for v in maps[name])==bytes(v==1 for v in maps[names[0]]),('density changed water',name)
            settings=next(c['settings'] for c in cases if c['id']==name)
            if not settings['original_surface_relations']:assert maps[name]==maps[names[0]],('free density changed terrain',name)
            density_comparisons+=1
    for names in continuity_groups.values():
        for name in names[1:]:
            assert objectives[name]==objectives[names[0]],('terrain control moved objectives',name,names[0]);continuity_comparisons+=1
    for theme in ('DESERT','SWAMP','CANDY'):assert maps['round-medium']==maps['theme-'+theme],theme
    byid={r['id']:r for r in records}
    def delta(a,b):return sum(x!=y for x,y in zip(maps[a],maps[b]))/len(maps[a])
    response={}
    for shape in SHAPES:
        assert len({maps[shape+'-'+level] for level in LEVELS})==5,shape
        response[shape+'_small_to_ultra']=delta(shape+'-small',shape+'-ultra')
        response[shape+'_narrow_to_wide']=delta(shape+'-narrow',shape+'-wide')
        assert response[shape+'_small_to_ultra']>=.05,response
        assert response[shape+'_narrow_to_wide']>=.04,response
        assert byid[shape+'-medium']['placed']>=24,('insufficient default colony opportunities',shape)
    if 'response-round-wide-ultra-small' in maps:
        for shape in SHAPES:
            for width in WIDTHS:
                for water in ('standard','ultra'):
                    prefix=f'response-{shape}-{width}-{water}-'
                    assert len({maps[prefix+level] for level in LEVELS})==5,prefix
                    changed=delta(prefix+'small',prefix+'ultra');assert changed>=.05,(prefix,changed)
                    # At maximum water the shoreline itself must still change, not only modifier colors.
                    shoreline=sum((a==1)!=(b==1) for a,b in zip(maps[prefix+'small'],maps[prefix+'ultra']))/len(maps[prefix+'small'])
                    assert shoreline>=.01,(prefix,'complexity erased from water',shoreline)
                    response[prefix+'small_to_ultra']=dict(native_change=changed,water_change=shoreline)
    response['round_to_square']=delta('round-small','square-small');assert response['round_to_square']>=.08,response
    for field,key,levels in (('water_amount','water',('low','high','extreme','ultra')),('gravel_moss_amount','surfaces',('low','high','extreme','ultra')),('neutral_colony_density','placed',('sparse','dense','extreme','ultra'))):
        values=[byid[field+'-'+level][key] for level in levels];assert values==sorted(values),(field,values)
    result=dict(status='PASS',ring_maps=len(records),old_exact_replays=len(cases)-len(records),density_comparisons=density_comparisons,
        objective_continuity_comparisons=continuity_comparisons,control_response=response,records=records)
    (folder/'verification.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k!='records'},indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('output',type=Path);p.add_argument('--wide',action='store_true');p.add_argument('--verify-only',action='store_true');p.add_argument('--workers',type=int,default=6)
    args=p.parse_args();args.output=args.output.resolve();args.output.mkdir(parents=True,exist_ok=True);cases=schedule(args.wide)
    (args.output/'cases.json').write_text(json.dumps(cases,indent=2))
    if not args.verify_only:CR.generate(args.output,cases,args.workers)

    verify(args.output,cases)
