"""Native checks for mirrored Regions V17, continuity and exact V16 preservation.
Usage: python Verify-RegionsPvp.py OUTPUT [--verify-only]
"""
import argparse, copy, hashlib, json, os, re, subprocess, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
KEYS = ('ants','beetles','scorpions','spiders','wasps')

def schedule():
    cases=[]
    base=dict(schema_version=11,preset='balanced',layout_family='natural-landscape-pvp',mirroring_axes=2,
        seed='397716241463670640',size='256,256',players=4,terrain_complexity='medium',water_amount='standard',
        gravel_moss_amount='standard',neutral_colony_density='standard',original_surface_relations=True,
        prevent_colony_overlapping=True,neutral_colony_weights=dict.fromkeys(KEYS,100),starting_colony_shares=[0,10,20,30])
    def add(name,**changes):
        settings=copy.deepcopy(base);settings.update(changes)
        if len(settings['starting_colony_shares'])!=settings['players']:settings['starting_colony_shares']=[0]*settings['players']
        cases.append(dict(id=name,settings=settings))
    for axes,players in ((1,6),(2,4),(4,8)):
        for level in ('small','medium','high','extreme','ultra'):
            add(f'axes{axes}-{level}',mirroring_axes=axes,players=players,terrain_complexity=level)
    for theme in ('DESERT','SWAMP','CANDY'):
        add('theme-'+theme,tileset=theme,mirroring_axes=4,players=8)
    for axes,players in ((1,2),(2,4)):
        for seed in ('0','1','2','397716241463670640'):
            add(f'64-a{axes}-s{seed}',size='64,64',mirroring_axes=axes,players=players,seed=seed)
    add('128-six-horizontal',size='128,128',mirroring_axes=1,players=6,seed='1',tileset='DESERT',starting_colony_mode='random')
    add('128-eight-ultra',size='128,128',mirroring_axes=4,players=8,seed='0',tileset='CANDY',terrain_complexity='ultra',
        water_amount='ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',prevent_colony_overlapping=False,original_surface_relations=False)
    for strict in (True,False):
        add('512-ultra-'+str(strict),size='512,512',mirroring_axes=4,players=8,terrain_complexity='ultra',
            water_amount='ultra',gravel_moss_amount='ultra',neutral_colony_density='ultra',prevent_colony_overlapping=strict,
            original_surface_relations=strict,starting_colony_shares=[100]*8,starting_colony_mode='random')
    add('wasps-only',mirroring_axes=1,players=2,seed='18446744073709551615',neutral_colony_weights=dict.fromkeys(KEYS,0)|{'wasps':100})
    add('no-colonies',neutral_colony_weights=dict.fromkeys(KEYS,0))
    add('ownership-random',starting_colony_mode='random')
    baseline=ROOT/'artifacts/rmg/regions-biomes/native-01'
    for name in ('NORMAL-small','NORMAL-medium','NORMAL-high','DESERT-high','SWAMP-medium','CANDY-ultra'):
        cases.append(dict(id='replay-'+name,settings=json.loads((baseline/name/'settings.json').read_text()),baseline=str(baseline/name/'map.oramap')))
    baseline=ROOT/'artifacts/rmg/regions-512/native-01/512-NORMAL'
    cases.append(dict(id='replay-512',settings=json.loads((baseline/'settings.json').read_text()),baseline=str(baseline/'map.oramap')))
    return cases

def members(path):
    with zipfile.ZipFile(path) as z:return {n:z.read(n) for n in z.namelist()}

def actors(path):
    text=members(path)['map.yaml'].decode('utf-8-sig').replace('\r','')
    result=[]
    for actor in re.finditer(r'^\tActor\d+: ([^\n]+)\n((?:\t\t[^\n]+\n)*)',text,re.M):
        point=re.search(r'Location: (\d+),(\d+)',actor[2])
        if point:result.append((actor[1],int(point[1])-2,int(point[2])-2))
    return result

def orbit(x,y,n,axes,seed):
    if axes==1:return {(x,y),(n-1-x,y) if seed%2==0 else (x,n-1-y)}
    cells={(x,y),(n-1-x,y),(x,n-1-y),(n-1-x,n-1-y)}
    return cells|{(b,a) for a,b in cells} if axes==4 else cells

def generate(folder,cases):
    env=os.environ.copy();env.update(DOTNET_ROOT=str(ROOT/'.tools/dotnet'),DOTNET_MULTILEVEL_LOOKUP='0',ENGINE_DIR='..',MOD_SEARCH_PATHS=str(ROOT/'mods')+',./mods')
    tool=str(ROOT/'engine/bin/OpenRA.Utility.exe')
    for case in cases:
        out=folder/case['id'];out.mkdir();(out/'settings.json').write_text(json.dumps(case['settings'],indent=2))
        args=[tool,'sa','--generate-sa-map',str(out/'map.oramap'),'--player-settings',str(out/'settings.json'),'--report',str(out/'report.json'),'--verify-repeatability']
        run=subprocess.run(args,cwd=ROOT/'engine',env=env,capture_output=True,timeout=180)
        log=run.stdout+run.stderr;(out/'generate.log').write_bytes(log)
        if run.returncode:
            assert case['settings']['size']=='64,64' and b'starts in safe mirrored groups' in log,(case['id'],log.decode(errors='replace'))
            repeat=subprocess.run(args,cwd=ROOT/'engine',env=env,capture_output=True,timeout=180)
            assert repeat.returncode and b'starts in safe mirrored groups' in repeat.stdout+repeat.stderr,case['id']
            (out/'rejection-repeat.log').write_bytes(repeat.stdout+repeat.stderr)
            print(case['id'],'safe repeated capacity rejection',flush=True);continue
        run=subprocess.run([tool,'sa','--compare-natural-terrain','--reference',str(out/'map.oramap'),str(out/'actual')],cwd=ROOT/'engine',env=env,capture_output=True,timeout=180)
        (out/'export.log').write_bytes(run.stdout+run.stderr);assert run.returncode==0,(case['id'],run.stderr)
        print(case['id'],'native package passed',flush=True)

def verify(folder,cases):
    records=[];rejected=[]
    for case in cases:
        out=folder/case['id'];s=case['settings']
        if not (out/'report.json').exists():
            assert s['size']=='64,64' and (out/'rejection-repeat.log').exists() and not (out/'map.oramap').exists(),case['id']
            rejected.append(case['id']);continue
        r=json.loads((out/'report.json').read_text());v=r['native_validation'];g=r['regions']
        assert v['accepted'] and r['package_validation']['map_yaml_lint']=='passed' and r['performance']['repeatability_checked'],case['id']
        assert all(v[k]==0 for k in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),case['id']
        native=(out/'actual/semantic.u8').read_bytes();n=int(s['size'].split(',')[0]);a=actors(out/'map.oramap')
        assert len(native)==n*n and hashlib.sha256(native).hexdigest()==g['semantic_sha256'],case['id']
        assert sum(t=='mpspawn' for t,x,y in a)==s['players'],case['id']
        if 'baseline' in case:assert members(out/'map.oramap')==members(case['baseline']),case['id']
        else:
            axes=s['mirroring_axes'];seed=int(s['seed']);group=8 if axes==4 else 2*axes
            for y in range(n):
                for x in range(n):assert all(native[y*n+x]==native[b*n+c] for c,b in orbit(x,y,n,axes,seed)),(case['id'],x,y)
            for t in {t for t,x,y in a}:
                points={(x,y) for kind,x,y in a if kind==t}
                assert all(orbit(x,y,n,axes,seed)<=points for x,y in points),(case['id'],t)
            count=sum(t.endswith('_colony') for t,x,y in a)
            assert count==g['neutral_colonies_placed'] and count%group==0 and count<=g['neutral_colonies_requested'],case['id']
            assert all(s['neutral_colony_weights'][t.removesuffix('_colony')]>0 for t,x,y in a if t.endswith('_colony')),case['id']
        records.append(dict(id=case['id'],placed=g['neutral_colonies_placed'],target=g['neutral_colonies_requested'],generation_ms=r['performance']['logical_generation_ms']))
    normal=members(folder/'axes4-medium/map.oramap')['map.bin']
    for theme in ('DESERT','SWAMP','CANDY'):
        assert normal==members(folder/('theme-'+theme)/'map.oramap')['map.bin'],theme
        assert [(x,y) for t,x,y in actors(folder/'axes4-medium/map.oramap')]==[(x,y) for t,x,y in actors(folder/('theme-'+theme)/'map.oramap')],theme
    assert actors(folder/'axes2-medium/map.oramap')==actors(folder/'ownership-random/map.oramap')
    continuity=[]
    for axes in (1,2,4):
        low=(folder/f'axes{axes}-small/actual/semantic.u8').read_bytes()
        high=(folder/f'axes{axes}-ultra/actual/semantic.u8').read_bytes()
        water=sum(a==b==1 for a,b in zip(low,high))/sum(a==1 for a in low)
        # Ultra intentionally uses the accepted V13 detail strengths. Record spatial
        # retention, but do not invent a minimum shared-cell percentage. The native
        # runtime harness separately checks exact priority parity with Natural.
        assert low!=high,axes
        continuity.append(dict(axes=axes,low_water_retained_at_ultra=water))
    assert any(r['id'].startswith('64-a2-') for r in records),'No playable four-player 64 map.'
    result=dict(status='PASS',accepted=len(records),safe_capacity_rejections=rejected,exact_baseline_replays=7,continuity=continuity,cases=records)
    (folder/'verification.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2),flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('output',type=Path);p.add_argument('--verify-only',action='store_true');args=p.parse_args();folder=args.output.resolve();cases=schedule()
    if not args.verify_only:
        folder.mkdir(parents=True,exist_ok=True)
        if any(folder.iterdir()):raise ValueError('Choose a new output directory.')
        (folder/'manifest.json').write_text(json.dumps(cases,indent=2));generate(folder,cases)
    verify(folder,cases)
