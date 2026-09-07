"""Verify V16 native 64/128/256 packages, ownership invariance and frozen V15 replay.
Usage: python Verify-RegionsOwnership.py OUTPUT_DIRECTORY [--verify-only]
Uses the standard library; choose a fresh output directory.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('players', Path(__file__).with_name('Verify-RegionsPlayers.py'))
players = importlib.util.module_from_spec(spec)
spec.loader.exec_module(players)
read = players.read

def schedule():
    cases = []
    def add(name, size=64, count=4, **changes):
        settings = dict(schema_version=10, preset='balanced', layout_family='natural-landscape',
            seed='397716241463670640', size=f'{size},{size}', players=count, terrain_complexity='medium',
            water_amount='standard', gravel_moss_amount='standard', neutral_colony_density='standard',
            original_surface_relations=True, prevent_colony_overlapping=True,
            neutral_colony_weights=dict.fromkeys(players.KEYS, 100), starting_colony_shares=[0]*count)
        settings.update(changes)
        cases.append(dict(id=name, settings=settings))
    for seed in ('397716241463670640', '0'):
        for count in range(1,9): add(f'64-{seed}-players-{count}', count=count, seed=seed)
    for count in (1,4,8):
        for original in (True,False):
            add(f'64-ultra-{count}-{original}', count=count, terrain_complexity='ultra', water_amount='ultra',
                gravel_moss_amount='ultra', neutral_colony_density='ultra', original_surface_relations=original,
                prevent_colony_overlapping=False, starting_colony_shares=[100]*count)
    for size in (64,128,256):
        for name, shares in [('zero',[0,0,0]),('percent',[0,10,50]),('weighted',[40,40,80])]:
            add(f'{size}-{name}', size=size, count=3, starting_colony_shares=shares)
    return cases

def generate(folder, cases):
    env = os.environ.copy()
    env.update(DOTNET_ROOT=str(ROOT/'.tools/dotnet'), DOTNET_MULTILEVEL_LOOKUP='0', ENGINE_DIR='..', MOD_SEARCH_PATHS=str(ROOT/'mods')+',./mods')
    utility = str(ROOT/'engine/bin/OpenRA.Utility.exe')
    for case in cases:
        out = folder/case['id']
        out.mkdir()
        (out/'settings.json').write_text(json.dumps(case['settings'],indent=2))
        args=[utility,'sa','--generate-sa-map',str(out/'map.oramap'),'--player-settings',str(out/'settings.json'),
              '--report',str(out/'report.json'),'--verify-repeatability']
        result=subprocess.run(args,cwd=ROOT/'engine',env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=120)
        (out/'generate.log').write_bytes(result.stdout)
        if result.returncode:
            # Deliberate over-limit input and insufficient protected start space must reject. Other failures are regressions.
            log=result.stdout.decode('utf-8-sig')
            assert ('The completed terrain supports only ' in log and 'locally valid starts.' in log) or ('64x64 supports 1 through 4 players.' in log and case['settings']['players'] > 4), (case['id'], log)
            assert not (out/'map.oramap').exists() and not (out/'report.json').exists(), case['id']
            retry=subprocess.run(args,cwd=ROOT/'engine',env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=120)
            (out/'rejection-repeat.log').write_bytes(retry.stdout)
            assert retry.returncode and result.stdout==retry.stdout, case['id']
            print(case['id'],'safe repeated rejection',flush=True)
            continue
        result=subprocess.run([utility,'sa','--compare-natural-terrain','--reference',str(out/'map.oramap'),str(out/'actual')],
            cwd=ROOT/'engine',env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=120)
        (out/'export.log').write_bytes(result.stdout)
        assert result.returncode==0,case['id']
        print(case['id'],'native package passed',flush=True)

def allocation(n, shares):
    total=sum(shares)
    if not total: return [0]*len(shares)
    budget=(n*min(total,100)+99)//100
    counts=[budget*s//total for s in shares]
    rank=sorted(range(len(shares)), key=lambda i: (-(budget*shares[i]%total),i))
    for i in rank[:budget-sum(counts)]: counts[i]+=1
    return counts

def verify(folder,cases):
    accepted=[]
    rejected=[]
    for case in cases:
        out=folder/case['id']
        if not (out/'report.json').exists():
            assert (out/'rejection-repeat.log').read_bytes()==(out/'generate.log').read_bytes(),case['id']
            assert not (out/'map.oramap').exists(),case['id']
            rejected.append(case['id'])
            continue
        report=read(out/'report.json')
        ref=read(out/'actual/reference.json')
        native=report['native_validation']
        regions=report['regions']
        assert native['accepted'] and report['generator_version']==16,case['id']
        assert report['package_validation']['map_yaml_lint']=='passed' and report['performance']['repeatability_checked'],case['id']
        assert len(ref['player_starts'])==case['settings']['players'],case['id']
        assert len(ref['neutral_preview_positions'])==regions['neutral_colonies_placed'],case['id']
        assert hashlib.sha256((out/'actual/semantic.u8').read_bytes()).hexdigest()==regions['semantic_sha256'],case['id']
        assert all(native[f]==0 for f in ('footprint_overlap_cells','production_exit_failures','invalid_start_cells','invalid_colony_cells')),case['id']
        shares=case['settings']['starting_colony_shares']
        assert regions['starting_colony_shares']==shares,case['id']
        counts=allocation(regions['neutral_colonies_placed'],shares)
        assert regions['starting_colonies_allocated_if_all_slots_occupied']==counts,case['id']
        assert regions['unowned_colonies_if_all_slots_occupied']==regions['neutral_colonies_placed']-sum(counts),case['id']
        with zipfile.ZipFile(out/'map.oramap') as z:
            yaml=z.read('map.yaml').decode('utf-8-sig')
            assert 'RmgStartingColonyOwnership:' in yaml and 'PlayerShares: '+', '.join(map(str,shares)) in yaml,case['id']
        accepted.append(dict(id=case['id'],placed=regions['neutral_colonies_placed'],target=regions['neutral_colonies_requested'],allocated=counts))
    for size in (64,128,256):
        base=folder/f'{size}-zero'
        for mode in ('percent','weighted'):
            other=folder/f'{size}-{mode}'
            assert (base/'actual/semantic.u8').read_bytes()==(other/'actual/semantic.u8').read_bytes()
            assert players.options.actors(base/'map.oramap')==players.options.actors(other/'map.oramap')
            assert read(base/'actual/reference.json')['player_starts']==read(other/'actual/reference.json')['player_starts']
    assert any(a['id']=='64-397716241463670640-players-1' for a in accepted)
    range_rejections=sum(next(c for c in cases if c['id']==name)['settings']['players'] > 4 for name in rejected)
    summary=dict(status='PASS',accepted=len(accepted),range_rejections=range_rejections,capacity_rejections=len(rejected)-range_rejections,safe_rejections=rejected,cases=accepted)
    (folder/'verification.json').write_text(json.dumps(summary,indent=2))
    print('V16 verified:',len(accepted),'packages;',len(rejected),'safe rejections',flush=True)

def replay(folder):
    frozen=ROOT/'artifacts/rmg/regions-v15/native-matrix'
    manifest=[c for c in read(frozen/'manifest.json') if 'expected_rejection' not in c]
    folder.mkdir()
    players.options.extended.generate(folder,manifest)
    for case in manifest:
        old,new=read(frozen/case['id']/'report.json'),read(folder/case['id']/'report.json')
        keys=[k for k in old if 'sha256' in k]
        assert keys and all(old[k]==new[k] for k in keys),case['id']
        assert (frozen/case['id']/'actual/semantic.u8').read_bytes()==(folder/case['id']/'actual/semantic.u8').read_bytes(),case['id']
    (folder/'verification.json').write_text(json.dumps(dict(status='FROZEN_V15_UNCHANGED',cases=len(manifest)),indent=2))
    print('Frozen V15 exact replays:',len(manifest),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output',type=Path)
    parser.add_argument('--verify-only',action='store_true')
    parser.add_argument('--replay-v15',action='store_true')
    args=parser.parse_args()
    folder=args.output.resolve()
    if args.verify_only: verify(folder,read(folder/'manifest.json'))
    else:
        if folder.exists() and any(folder.iterdir()): parser.error('Choose a fresh output directory.')
        folder.mkdir(parents=True,exist_ok=True)
        cases=schedule()
        (folder/'manifest.json').write_text(json.dumps(cases,indent=2))
        generate(folder,cases)
        verify(folder,cases)
        if args.replay_v15: replay(folder/'frozen-v15')
