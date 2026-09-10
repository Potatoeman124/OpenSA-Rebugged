"""Divided Lands native topology, continuity, quantity and historical replay checks."""
import argparse,copy,hashlib,json,math,sys
from pathlib import Path
from importlib.util import module_from_spec,spec_from_file_location
sys.dont_write_bytecode=True
SPEC=spec_from_file_location('ring_checks',Path(__file__).with_name('Verify-Ring.py'))
RING=module_from_spec(SPEC);SPEC.loader.exec_module(RING)
CR=RING.CR;COMMON=RING.COMMON;ROOT=RING.ROOT;LEVELS=RING.LEVELS
CROSSINGS=('none','one','two');WIDTHS=('narrow','standard','wide')

def schedule(wide=False):
    base=dict(schema_version=15,preset='balanced',layout_family='divided-lands',seed='397716241463670640',size='256,256',players=4,
        terrain_complexity='medium',land_crossings='one',crossing_width='standard',water_amount='standard',gravel_moss_amount='standard',
        neutral_colony_density='standard',original_surface_relations=True,prevent_colony_overlapping=True,
        neutral_colony_weights=dict.fromkeys(COMMON.KEYS,100),starting_colony_shares=[0,10,20,30])
    cases=[]
    def add(name,**changes):
        s=copy.deepcopy(base);s.update(changes)
        if len(s['starting_colony_shares'])!=s['players']:s['starting_colony_shares']=[0]*s['players']
        cases.append(dict(id=name,settings=s))
    for players in (2,4,8):
        for crossing in CROSSINGS:
            for level in LEVELS:add(f'p{players}-{crossing}-{level}',players=players,land_crossings=crossing,terrain_complexity=level)
            for width in WIDTHS:add(f'routes-{players}-{crossing}-{width}',players=players,land_crossings=crossing,crossing_width=width)
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
        seeds=('0','1','18446744073709551615')
        # Full size/player/width/crossing/complexity product, under maximum colony pressure.
        for size in (64,128,256,512):
            for players in ((2,4) if size==64 else (2,4,8)):
                for si,crossing in enumerate(CROSSINGS):
                    for wi,width in enumerate(WIDTHS):
                        for ci,level in enumerate(LEVELS):
                            add(f'geometry-{size}-{players}-{crossing}-{width}-{level}',size=f'{size},{size}',players=players,land_crossings=crossing,crossing_width=width,terrain_complexity=level,
                                seed=seeds[(si+wi+ci)%3],water_amount='low' if ci%2==0 else 'ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',prevent_colony_overlapping=False)
        for crossing in CROSSINGS:
            for water in ('low','standard','high','extreme','ultra'):
                for surface in ('low','standard','high','extreme','ultra'):
                    add(f'quantities-{crossing}-{water}-{surface}',land_crossings=crossing,terrain_complexity='ultra',water_amount=water,gravel_moss_amount=surface,
                        neutral_colony_density='ultra',prevent_colony_overlapping=False,original_surface_relations=False)
            for original in (True,False):
                for width in WIDTHS:
                    for density in ('sparse','standard','dense','extreme','ultra'):
                        add(f'density-{crossing}-{width}-{density}-{original}',land_crossings=crossing,crossing_width=width,terrain_complexity='ultra',water_amount='ultra',gravel_moss_amount='ultra',
                            neutral_colony_density=density,prevent_colony_overlapping=False,original_surface_relations=original)
            for players in (2,4,8):
                for level in LEVELS:
                    add(f'response-{players}-{crossing}-{level}',players=players,land_crossings=crossing,crossing_width='wide',terrain_complexity=level,
                        water_amount='ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',prevent_colony_overlapping=False,original_surface_relations=False)
    for crossing in CROSSINGS:
        for level in LEVELS:
            add(f'shore-review-{crossing}-{level}',seed='825300756769842102',players=8,land_crossings=crossing,crossing_width='narrow',terrain_complexity=level,
                water_amount='ultra',gravel_moss_amount='low',neutral_colony_density='extreme',prevent_colony_overlapping=False,original_surface_relations=False)
    cases.extend(c for c in RING.schedule() if 'baseline' in c)
    old=ROOT/'artifacts/rmg/ring/matrix-final'
    for name in ('round-medium','square-ultra','octagonal-narrow','64-p4','128-p8','512-ultra-False'):
        cases.append(dict(id='replay-ring-'+name,settings=json.loads((old/name/'settings.json').read_text()),baseline=str(old/name/'map.oramap')))
    return cases

def seam_crossings(native,n,players,seed):
    center=(n-1)/2;angles=[math.pi/2 if seed%2==0 else 0] if players==2 else [i*2*math.pi/players for i in range(players)]
    counts=[]
    for angle in angles:
        dx,dy=math.cos(angle),math.sin(angle);limit=int(center/max(abs(dx),abs(dy)))
        values=[native[round(center+k*dy)*n+round(center+k*dx)]!=1 for k in range(-limit+2 if players==2 else 0,limit-1)]
        counts.append(sum(v and (i==0 or not values[i-1]) for i,v in enumerate(values)))
    return counts


def isolated_homes(native,n,s,targets):
    from collections import deque
    center=(n-1)/2;p=s['players'];vertical=int(s['seed'])%2==0
    gates=[] if s['land_crossings']=='none' else ([0] if s['land_crossings']=='one' else [-.26*n,.26*n]) if p==2 else ([.32*n] if s['land_crossings']=='one' else [.20*n,.43*n] if n==64 else [.23*n,.40*n])
    width=(4,6,8)[WIDTHS.index(s['crossing_width'])] if n==64 else (6,8,10)[WIDTHS.index(s['crossing_width'])] if n==128 and p==8 else (6,10,16)[WIDTHS.index(s['crossing_width'])]
    land=bytearray(v!=1 for v in native)
    for i in range(n*n):
        x,y=abs(i%n-center),abs(i//n-center)
        distance=(x if vertical else y) if p==2 else min(x,y,abs(x-y)/math.sqrt(2)) if p==8 else min(x,y)
        along=(i//n-center if vertical else i%n-center) if p==2 else max(x,y)
        if distance<=4 and any(abs(along-g)<=width/2+3 for g in gates):land[i]=0
    groups=0
    for t,x,y in targets:
        if t!='mpspawn':continue
        at=y*n+x
        if not land[at]:return -1
        groups+=1;land[at]=0;queue=deque([at])
        while queue:
            i=queue.popleft();x,y=i%n,i//n
            for ny in range(max(0,y-1),min(n,y+2)):
                for nx in range(max(0,x-1),min(n,x+2)):
                    q=ny*n+nx
                    if land[q]:land[q]=0;queue.append(q)
    return groups


def shore_clearances(native,n,actors):
    import re
    distance=[0 if v==1 else n*2 for v in native]
    for indices,step in ((range(n*n),1),(range(n*n-1,-1,-1),-1)):
        for i in indices:
            x,y=i%n,i//n
            for nx,ny in ((x-step,y),(x-step,y-step),(x,y-step),(x+step,y-step)):
                if 0<=nx<n and 0<=ny<n:distance[i]=min(distance[i],distance[ny*n+nx]+1)
    values=[]
    for t,x,y in actors:
        if not t.endswith('_colony'):continue
        rules=(ROOT/'mods/sa/rules'/(t.removesuffix('_colony')+'-buildings.yaml')).read_text()
        footprint=re.search(r'Footprint: ([^\n]+)',rules)[1].split()
        values.append(min(distance[(y+dy)*n+x+dx] for dy,row in enumerate(footprint) for dx,ch in enumerate(row) if ch in 'xX+')-1)
    return values


def verify(folder,cases):
    records=[];maps={};objectives={};density_groups={};continuity_groups={};previous_water_comparisons=0
    for case in cases:
        name=case['id'];out=folder/name;s=case['settings']
        if 'baseline' in case:
            assert COMMON.members(out/'map.oramap')==COMMON.members(case['baseline']),name
            continue
        r=json.loads((out/'report.json').read_text());v=r['native_validation'];g=r['regions'];plan=g['divided_lands_plan']
        n=int(s['size'].split(',')[0]);axes={2:1,4:2,8:4}[s['players']];seed=int(s['seed'])
        assert v['accepted'] and r['performance']['repeatability_checked'] and r['package_validation']['map_yaml_lint']=='passed',name
        assert v['divided_lands_home_access'] and v['divided_lands_weighted_pool_parity'],name
        assert all(d>=2 for d in v['divided_lands_colony_shore_clearance_native']),name
        expected=CROSSINGS.index(s['land_crossings'])
        assert v['divided_lands_all_connected']==(expected>0),name
        assert v['divided_lands_home_components']==(1 if expected else s['players']),name
        for topology in (plan['topology'],v['divided_lands_occupied_topology']):
            assert topology['isolated_home_territories']==s['players'] and topology['unplanned_bypasses']==0,name
            assert topology['crossings_per_border']==[expected]*(1 if s['players']==2 else s['players']),name
        assert all(v[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),name
        native=(out/'actual/semantic.u8').read_bytes();actors=COMMON.actors(out/'map.oramap')
        previous=ROOT/'artifacts/rmg/divided-lands/matrix-final'/name/'actual/semantic.u8'
        if previous.exists():
            old=previous.read_bytes();assert bytes(v==1 for v in old)==bytes(v==1 for v in native),(name,'shore placement changed water')
            previous_water_comparisons+=1
            if not s['original_surface_relations']:assert old==native,(name,'shore placement changed free surfaces')
        if name.startswith('shore-review-') or name in ('64-p4','128-p8','512-ultra-False'):
            assert sorted(shore_clearances(native,n,actors))==sorted(v['divided_lands_colony_shore_clearance_native']),(name,'shore clearance disagreement')
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
        assert seam_crossings(native,n,s['players'],seed)==[expected]*(1 if s['players']==2 else s['players']),name
        if s['players']>=4:assert native[n//2*n+n//2]==1,(name,'central land junction')
        # Flood exported native cells with intended gates closed, independently of the native validator.
        if name.startswith('p') or name in ('64-p2','64-p4','128-p8','512-p8'):
            assert isolated_homes(native,n,s,targets)==s['players'],(name,'bypass')
        maps[name]=native;objectives[name]=targets;m=g['metrics']
        records.append(dict(id=name,placed=len(colonies),target=g['neutral_colonies_requested'],generation_ms=r['performance']['logical_generation_ms'],
            water=m['water_percent_map'],surfaces=m['gravel_percent_land']+m['moss_percent_land']))
        options=copy.deepcopy(s)
        for key in ('neutral_colony_density','prevent_colony_overlapping','neutral_colony_weights','starting_colony_shares','starting_colony_mode'):
            options.pop(key,None)
        density_groups.setdefault(json.dumps(options,sort_keys=True),[]).append(name)
        options=copy.deepcopy(s)
        for key in ('terrain_complexity','water_amount','gravel_moss_amount','crossing_width','land_crossings','original_surface_relations','starting_colony_shares','starting_colony_mode','tileset'):
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
    for theme in ('DESERT','SWAMP','CANDY'):assert maps['p4-one-medium']==maps['theme-'+theme],theme
    byid={r['id']:r for r in records}
    def delta(a,b):return sum(x!=y for x,y in zip(maps[a],maps[b]))/len(maps[a])
    response={}
    for players in (2,4,8):
        for crossing in CROSSINGS:
            prefix=f'p{players}-{crossing}-'
            assert len({maps[prefix+level] for level in LEVELS})==5,prefix
            changed=delta(prefix+'small',prefix+'ultra');assert changed>=.08,(prefix,changed)
            response[prefix+'small_to_ultra']=changed
            assert byid[prefix+'medium']['placed']>=players*5,('insufficient default colony opportunities',prefix)
            a=f'routes-{players}-{crossing}-narrow';b=f'routes-{players}-{crossing}-wide'
            changed=delta(a,b)
            if crossing=='none':assert changed==0,(a,b,changed)
            else:assert changed>=.01,(a,b,changed)
            response[f'p{players}-{crossing}-narrow_to_wide']=changed
            prefix=f'response-{players}-{crossing}-'
            if prefix+'small' in maps:
                assert len({maps[prefix+level] for level in LEVELS})==5,prefix
                changed=delta(prefix+'small',prefix+'ultra');assert changed>=.08,(prefix,changed)
                shoreline=sum((a==1)!=(b==1) for a,b in zip(maps[prefix+'small'],maps[prefix+'ultra']))/len(maps[prefix+'small'])
                assert shoreline>=.01,(prefix,'complexity erased from water',shoreline)
                response[prefix+'small_to_ultra']=dict(native_change=changed,water_change=shoreline)
    for field,key,levels in (('water_amount','water',('low','high','extreme','ultra')),('gravel_moss_amount','surfaces',('low','high','extreme','ultra')),('neutral_colony_density','placed',('sparse','dense','extreme','ultra'))):
        values=[byid[field+'-'+level][key] for level in levels];assert values==sorted(values),(field,values)
    result=dict(status='PASS',divided_lands_maps=len(records),old_exact_replays=len(cases)-len(records),density_comparisons=density_comparisons,
        objective_continuity_comparisons=continuity_comparisons,previous_water_comparisons=previous_water_comparisons,control_response=response,records=records)
    (folder/'verification.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k!='records'},indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('output',type=Path);p.add_argument('--wide',action='store_true');p.add_argument('--verify-only',action='store_true');p.add_argument('--workers',type=int,default=6)
    args=p.parse_args();args.output=args.output.resolve();args.output.mkdir(parents=True,exist_ok=True);cases=schedule(args.wide)
    (args.output/'cases.json').write_text(json.dumps(cases,indent=2))
    if not args.verify_only:CR.generate(args.output,cases,args.workers)

    verify(args.output,cases)
