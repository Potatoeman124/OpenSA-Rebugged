"""Run a frozen, fresh-seed acceptance schedule; stop on the first failed generation."""
import argparse, json, os, pathlib, secrets, subprocess, time
root = pathlib.Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser()
parser.add_argument("--batch",default="regions-final-01")
args=parser.parse_args()
if not args.batch.replace("-","").replace("_","").isalnum(): raise SystemExit("Use a simple batch name.")
out=root/"artifacts/rmg/reassessment-comparison"/args.batch
out.mkdir(parents=True,exist_ok=True)
manifest=out/"manifest.json"
if manifest.exists(): raise SystemExit("Retain the earlier batch. Use a new batch name after any correction.")
env=os.environ.copy()
env.update(DOTNET_ROOT=str(root/".tools/dotnet"),DOTNET_MULTILEVEL_LOOKUP="0",
           ENGINE_DIR="..",MOD_SEARCH_PATHS=str(root/"mods")+",./mods")
exe=str(root/"engine/bin/OpenRA.Utility.exe")
schedule=[(128,2,"low"),(256,4,"standard"),(128,4,"high"),(256,2,"low"),(128,4,"standard"),
          (256,4,"high"),(128,2,"standard"),(256,2,"high"),(128,4,"low"),(256,4,"low"),
          (128,2,"high"),(256,2,"standard"),(128,4,"high"),(256,4,"standard"),(256,4,"high")]
cases=[]
for index,(size,players,complexity) in enumerate(schedule,1):
    settings=dict(schema_version=5,preset="balanced",seed=str(secrets.randbits(64)),players=players,
        size=f"{size},{size}",layout_family="natural-landscape",terrain_complexity=complexity,
        water_amount="standard",gravel_moss_amount="standard",neutral_colony_density="standard",
        original_surface_relations=True)
    if index in (13,14): settings["original_surface_relations"]=False
    if index in (13,15): settings.update(water_amount="high",gravel_moss_amount="high",neutral_colony_density="dense")
    cases.append(dict(id=f"{index:02d}-{size}-{players}p-{complexity}",settings=settings))
manifest.write_text(json.dumps(dict(status="SCHEDULED_BEFORE_GENERATION",cases=cases),indent=2),encoding="utf-8")
results=[]
def run(command,log):
    begin=time.monotonic()
    process=subprocess.run([exe,"sa"]+command,cwd=root/"engine",env=env,capture_output=True)
    log.write_bytes(process.stdout+process.stderr)
    if process.returncode: raise RuntimeError(f"{log}: exit {process.returncode}")
    return round(time.monotonic()-begin,3)
# Keep this generation sequence uninterrupted. Rendering and review follow the completed batch.
for case in cases:
    folder=out/case["id"];folder.mkdir()
    settings=folder/"settings.json";settings.write_text(json.dumps(case["settings"],indent=2),encoding="utf-8")
    elapsed=run(["--generate-sa-map",str(folder/"map.oramap"),"--player-settings",str(settings),
                "--report",str(folder/"report.json")],folder/"generation.log")
    report=json.loads((folder/"report.json").read_text(encoding="utf-8-sig"))
    results.append(dict(id=case["id"],process_seconds=elapsed,uid=report["openra_uid"],
        performance=report["performance"],regions=report["regions"],native_validation=report["native_validation"]))
    (out/"results.json").write_text(json.dumps(results,indent=2),encoding="utf-8")
    print(case["id"],"GENERATED",report["performance"]["total_ms"],"ms",flush=True)
for case in cases:
    folder=out/case["id"]
    run(["--compare-natural-terrain","--reference",str(folder/"map.oramap"),str(folder/"actual")],folder/"textures.log")
    run(["--compare-natural-terrain","--player-evidence",str(folder/"settings.json"),str(folder/"construction")],folder/"construction.log")
    report=json.loads((folder/"report.json").read_text(encoding="utf-8-sig"))
    terrain=json.loads((folder/"construction/report.json").read_text(encoding="utf-8-sig"))
    assert report["regions"]["semantic_sha256"]==terrain["semantic_sha256"], "Evidence regeneration changed terrain"
    assert (folder/"actual/semantic.u8").read_bytes()==(folder/"construction/semantic.u8").read_bytes(), "Actual package terrain differs"
    reference=json.loads((folder/"actual/reference.json").read_text(encoding="utf-8-sig"))
    assert len(reference["neutral_preview_positions"])==report["regions"]["neutral_colonies_placed"]
    assert len(reference["player_starts"])==case["settings"]["players"]
    print(case["id"],"ACTUAL TEXTURES / INTENT / POSITIONS VERIFIED",flush=True)
(out/"completion.json").write_text(json.dumps(dict(status="15_GENERATED_AND_EXPORTED_PENDING_VISUAL_REVIEW",
    cases=len(cases),generation_attempts=15,retries=0),indent=2),encoding="utf-8")
