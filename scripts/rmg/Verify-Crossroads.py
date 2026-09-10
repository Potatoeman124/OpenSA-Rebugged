"""Crossroads native geometry, control response, gameplay and accepted-layout replay checks."""
import argparse,copy,json,sys
from pathlib import Path
from importlib.util import module_from_spec,spec_from_file_location
sys.dont_write_bytecode=True
SPEC=spec_from_file_location('battlefield',Path(__file__).with_name('Verify-ArtificialBattlefield.py'))
BF=module_from_spec(SPEC);SPEC.loader.exec_module(BF)
COMMON=BF.COMMON;ROOT=BF.ROOT
LEVELS=BF.LEVELS

def schedule():
    base=dict(schema_version=13,preset='balanced',layout_family='crossroads',seed='397716241463670640',size='256,256',players=4,
        terrain_complexity='medium',approach_width='standard',side_connections='standard',water_amount='standard',gravel_moss_amount='standard',
        neutral_colony_density='standard',original_surface_relations=True,prevent_colony_overlapping=True,
        neutral_colony_weights=dict.fromkeys(COMMON.KEYS,100),starting_colony_shares=[0,10,20,30])
    cases=[]
    def add(name,**changes):
        s=copy.deepcopy(base);s.update(changes)
        if len(s['starting_colony_shares'])!=s['players']:s['starting_colony_shares']=[0]*s['players']
        cases.append(dict(id=name,settings=s))
    for players in (4,8):
        for level in LEVELS:add(f'p{players}-{level}',players=players,terrain_complexity=level)
    for width in ('narrow','standard','wide'):
        for connections in ('none','standard','many'):add('routes-'+width+'-'+connections,approach_width=width,side_connections=connections)
    for field,levels in (('water_amount',('low','high','extreme','ultra')),('gravel_moss_amount',('low','high','extreme','ultra')),('neutral_colony_density',('sparse','dense','extreme','ultra'))):
        for value in levels:add(field+'-'+value,**{field:value})
    for size in (64,128):
        for players in ((2,4) if size==64 else (2,4,8)):
            add(f'{size}-p{players}',size=f'{size},{size}',players=players,seed='0')
    add('64-ultra',size='64,64',players=4,seed='1',terrain_complexity='ultra',water_amount='ultra',gravel_moss_amount='ultra',approach_width='wide',side_connections='many')
    add('128-eight-ultra',size='128,128',players=8,seed='18446744073709551615',terrain_complexity='ultra',water_amount='ultra',gravel_moss_amount='ultra')
    add('two-horizontal',players=2,seed='1')
    add('two-none',players=2,side_connections='none')
    for theme in ('DESERT','SWAMP','CANDY'):add('theme-'+theme,tileset=theme)
    add('512-eight',size='512,512',players=8,tileset='CANDY',starting_colony_shares=[100]*8,starting_colony_mode='random')
    for strict in (True,False):add('512-ultra-'+str(strict),size='512,512',players=8,terrain_complexity='ultra',water_amount='ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',prevent_colony_overlapping=strict,original_surface_relations=strict,side_connections='many')
    add('free-surfaces',original_surface_relations=False)
    add('random-ownership',starting_colony_mode='random')
    add('wasps-only',neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0)|dict(wasps=100))
    add('no-colonies',neutral_colony_weights=dict.fromkeys(COMMON.KEYS,0))
    cases.extend(c for c in BF.schedule() if 'baseline' in c)
    old=ROOT/'artifacts/rmg/artificial-battlefield/revision-native-01'
    for name in ('cut-corners-medium','rectangles-ultra','diamonds-small','review-diamonds-wide-ultra','64-p4-s0','128-eight-SWAMP','512-eight','512-ultra-False'):
        cases.append(dict(id='replay-battlefield-'+name,settings=json.loads((old/name/'settings.json').read_text()),baseline=str(old/name/'map.oramap')))
    return cases

def verify(folder,cases):
    records=[];maps={};objectives={}
    for case in cases:
        out=folder/case['id'];s=case['settings'];r=json.loads((out/'report.json').read_text())
        if 'baseline' in case:
            assert COMMON.members(out/'map.oramap')==COMMON.members(case['baseline']),case['id']
            continue
        v=r['native_validation'];g=r['regions'];plan=g['crossroads_plan'];n=int(s['size'].split(',')[0]);axes={2:1,4:2,8:4}[s['players']];seed=int(s['seed'])
        assert v['accepted'] and r['performance']['repeatability_checked'] and r['package_validation']['map_yaml_lint']=='passed',case['id']
        assert v['battlefield_ground_access'] and v['battlefield_weighted_pool_parity'],case['id']
        assert all(v[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),case['id']
        native=(out/'actual/semantic.u8').read_bytes();actors=COMMON.actors(out/'map.oramap')
        for y in range(n):
            for x in range(n):assert all(native[y*n+x]==native[b*n+a] for a,b in COMMON.orbit(x,y,n,axes,seed)),(case['id'],x,y)
        for kind in {t for t,x,y in actors}:
            points={(x,y) for t,x,y in actors if t==kind}
            assert all(COMMON.orbit(x,y,n,axes,seed)<=points for x,y in points),(case['id'],kind)
        targets=[a for a in actors if a[0]=='mpspawn' or a[0].endswith('_colony')]
        colonies=[a for a in targets if a[0].endswith('_colony')]
        assert len(targets)-len(colonies)==s['players'] and len(colonies)%s['players']==0,case['id']
        assert all(s['neutral_colony_weights'][t.removesuffix('_colony')]>0 for t,x,y in colonies),case['id']
        # The actual central junction and the centerline of every approach stay clear.
        center=n/2-1;radius=plan['junction_radius_native']
        for y in range(n):
            for x in range(n):
                if (x-center)**2+(y-center)**2<=radius**2:assert native[y*n+x]==0,(case['id'],'junction',x,y)
        for t,x,y in targets:
            if t!='mpspawn':continue
            steps=max(abs(x-center),abs(y-center))
            for k in range(int(steps)+1):
                px=round(center+(x-center)*k/max(1,steps));py=round(center+(y-center)*k/max(1,steps))
                assert native[py*n+px]==0,(case['id'],'approach',px,py)
        maps[case['id']]=native;objectives[case['id']]=targets;m=g['metrics']
        records.append(dict(id=case['id'],placed=len(colonies),target=g['neutral_colonies_requested'],central_colonies=plan['central_colonies'],generation_ms=r['performance']['logical_generation_ms'],
            water=m['water_percent_map'],surfaces=m['gravel_percent_land']+m['moss_percent_land'],protected_land=plan['protected_land_cells']))
    for case in cases:
        s=case['settings']
        if 'baseline' not in case and s['size']=='256,256' and s['players']==4 and s['seed']=='397716241463670640' and s['neutral_colony_weights']==dict.fromkeys(COMMON.KEYS,100) and s['neutral_colony_density']=='standard':
            assert objectives[case['id']]==objectives['p4-medium'],('terrain control moved objectives',case['id'])
    for theme in ('DESERT','SWAMP','CANDY'):assert maps['p4-medium']==maps['theme-'+theme],theme
    byid={r['id']:r for r in records}
    def delta(a,b):return sum(x!=y for x,y in zip(maps[a],maps[b]))/len(maps[a])
    response={}
    for players in (4,8):
        assert len({maps[f'p{players}-{level}'] for level in LEVELS})==5
        response[f'p{players}_small_to_ultra']=delta(f'p{players}-small',f'p{players}-ultra')
        assert response[f'p{players}_small_to_ultra']>=.10,response
        assert byid[f'p{players}-medium']['central_colonies']>=players,'Missing central objective group'
    response['approach_narrow_to_wide']=delta('routes-narrow-standard','routes-wide-standard')
    response['side_none_to_standard']=delta('routes-standard-none','routes-standard-standard')
    response['side_standard_to_many']=delta('routes-standard-standard','routes-standard-many')
    assert all(response[k]>=.03 for k in ('approach_narrow_to_wide','side_none_to_standard','side_standard_to_many')),response
    for field,key,levels in (('water_amount','water',('low','high','extreme','ultra')),('gravel_moss_amount','surfaces',('low','high','extreme','ultra')),('neutral_colony_density','placed',('sparse','dense','extreme','ultra'))):
        values=[byid[field+'-'+level][key] for level in levels];assert values==sorted(values),(field,values)
    result=dict(status='PASS',accepted_crossroads=len(records),exact_older_replays=sum('baseline' in c for c in cases),control_response=response,cases=records)
    (folder/'verification.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('output',type=Path);parser.add_argument('--verify-only',action='store_true');args=parser.parse_args();folder=args.output.resolve();cases=schedule()
    if not args.verify_only:
        folder.mkdir(parents=True,exist_ok=True)
        if any(folder.iterdir()):raise ValueError('Choose an empty output directory.')
        (folder/'manifest.json').write_text(json.dumps(cases,indent=2));BF.generate(folder,cases)
    verify(folder,cases)
