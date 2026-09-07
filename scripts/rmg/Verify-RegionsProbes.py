"""Paired player-path probes and frozen manifest for the Regions integration."""
import json, os, pathlib, secrets, subprocess, sys, time
root = pathlib.Path(__file__).resolve().parents[2]
out = root / "artifacts/rmg/reassessment-comparison/regions-probes"
out.mkdir(parents=True, exist_ok=True)
env = os.environ.copy()
env.update(DOTNET_ROOT=str(root/".tools/dotnet"), DOTNET_MULTILEVEL_LOOKUP="0",
           ENGINE_DIR="..", MOD_SEARCH_PATHS=str(root/"mods")+",./mods")
exe = root/"engine/bin/OpenRA.Utility.exe"
cases = []
for size in (128,256):
    seed = str(secrets.randbits(64))
    baseline = dict(schema_version=5,preset="balanced",seed=seed,players=4,size=f"{size},{size}",
        layout_family="natural-landscape",water_amount="standard",gravel_moss_amount="standard",
        terrain_complexity="standard",neutral_colony_density="standard",original_surface_relations=True)
    variants = [("standard",{}),("water-low",dict(water_amount="low")),("water-high",dict(water_amount="high")),
        ("gravel-low",dict(gravel_moss_amount="low")),("gravel-high",dict(gravel_moss_amount="high")),
        ("complexity-low",dict(terrain_complexity="low")),("complexity-high",dict(terrain_complexity="high")),
        ("relations-off",dict(original_surface_relations=False)),
        ("two-sparse",dict(players=2,neutral_colony_density="sparse")),
        ("dense-high",dict(neutral_colony_density="dense",terrain_complexity="high",water_amount="high",gravel_moss_amount="high"))]
    for name, overrides in variants:
        cases.append(dict(id=f"{size}-{name}",settings=baseline|overrides))
manifest=out/"manifest.json"
if manifest.exists():
    raise SystemExit("A probe manifest already exists; retain it and choose a new batch directory.")
manifest.write_text(json.dumps(dict(status="SCHEDULED_BEFORE_GENERATION",cases=cases),indent=2),encoding="utf-8")
results=[]
for case in cases:
    folder=out/case["id"]; folder.mkdir()
    settings=folder/"settings.json"; settings.write_text(json.dumps(case["settings"],indent=2),encoding="utf-8")
    args=[str(exe),"sa","--generate-sa-map",str(folder/"map.oramap"),"--player-settings",str(settings),
          "--report",str(folder/"report.json"),"--verify-repeatability"]
    begin=time.monotonic()
    run=subprocess.run(args,cwd=root/"engine",env=env,capture_output=True)
    (folder/"generation.log").write_bytes(run.stdout+run.stderr)
    row=dict(id=case["id"],exit_code=run.returncode,process_seconds=round(time.monotonic()-begin,3))
    if run.returncode==0:
        report=json.loads((folder/"report.json").read_text(encoding="utf-8-sig"))
        row.update(performance=report["performance"],regions=report["regions"],validation=report["native_validation"])
    results.append(row)
    (out/"results.json").write_text(json.dumps(results,indent=2),encoding="utf-8")
    print(case["id"],"PASS" if run.returncode==0 else "FAIL",row["process_seconds"],flush=True)
    if run.returncode: sys.exit(run.returncode)
