"""Produce descriptive measurements and image sheets; no scores or acceptance thresholds."""
import argparse, csv, hashlib, json
from collections import deque
from pathlib import Path
from statistics import mean, median
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument("root", type=Path)
args = parser.parse_args()
root = args.root.resolve()
font_path = "C:/Windows/Fonts/segoeui.ttf"
font = ImageFont.truetype(font_path, 22)
small = ImageFont.truetype(font_path, 17)
cases = []
for batch in ("pilot-256", "pilot-128", "diagnostics-256"):
    manifest = json.loads((root / batch / "manifest.json").read_text(encoding="utf-8-sig"))
    for case in manifest["cases"]:
        name = f'{case["size"]}-{case["seed"]}-{case["method"]}-{case["complexity"]}'
        directory = root / batch / name
        report = json.loads((directory / "report.json").read_text(encoding="utf-8"))
        w = report["size"]
        data = (directory / "semantic.u8").read_bytes()
        intent = (directory / "intent.u8").read_bytes()
        assert len(data) == w*w and hashlib.sha256(data).hexdigest() == report["semantic_sha256"]
        assert hashlib.sha256(intent).hexdigest() == report["intent_sha256"]
        # Manhattan distance from cells adjacent to another surface; outer frame is not a boundary.
        distances = [-1] * len(data)
        queue = deque()
        def neighbors(i):
            x, y = i % w, i // w
            if x: yield i-1
            if x+1 < w: yield i+1
            if y: yield i-w
            if y+1 < w: yield i+w
        for i, value in enumerate(data):
            if any(data[n] != value for n in neighbors(i)):
                distances[i] = 0
                queue.append(i)
        while queue:
            i = queue.popleft()
            for n in neighbors(i):
                if distances[n] < 0:
                    distances[n] = distances[i]+1
                    queue.append(n)
        def quantile(values, p):
            return sorted(values)[round((len(values)-1)*p)]
        transects = []
        for numerator in (1, 3, 5, 7):
            pos = w*numerator//8
            for axis in ("horizontal", "vertical"):
                values = data[pos*w:(pos+1)*w] if axis == "horizontal" else data[pos::w]
                transects.append(dict(axis=axis, coordinate_native=pos,
                    surface_changes=sum(a!=b for a,b in zip(values,values[1:]))))
        def areas(predicate):
            seen, sizes = set(), []
            for i, value in enumerate(data):
                if i in seen or not predicate(value): continue
                queue = deque([i]); seen.add(i); count=0
                while queue:
                    item=queue.popleft(); count+=1
                    for n in neighbors(item):
                        if n not in seen and predicate(data[n]): seen.add(n); queue.append(n)
                sizes.append(count)
            return sorted(sizes, reverse=True)
        diagnostics = dict(
            boundary_distance=dict(metric="Manhattan native cells; boundary-adjacent cells are zero; outer frame excluded",
                median=median(distances), p90=quantile(distances,.9), maximum=max(distances),
                dirt_p90=quantile([d for d,t in zip(distances,data) if t==0],.9)),
            fixed_geometry_transects=transects,
            mean_transect_surface_changes=mean(t["surface_changes"] for t in transects),
            water_component_areas_native=areas(lambda t:t==1),
            dry_component_areas_native=areas(lambda t:t!=1),
            actors="None: terrain-only experiment; doodads do not affect this analysis")
        (directory / "analysis.json").write_text(json.dumps(diagnostics, indent=2)+"\n")
        changes = Image.new("RGB",(w,w))
        changes.putdata([(240,80,125) if a!=b else (28,31,37) for a,b in zip(intent,data)])
        changes.save(directory / "materialization-changes.png")
        entry = dict(batch=batch, name=name, directory=directory, **report, analysis=diagnostics)
        cases.append(entry)

review = root / "review"
review.mkdir(exist_ok=True)
def sheet(items, name, columns, img_name, image_size, title):
    rows = (len(items)+columns-1)//columns
    gap, header, caption = 16, 66, 47
    width = columns*(image_size+gap)+gap
    height = rows*(image_size+caption+gap)+header+gap
    canvas = Image.new("RGB",(width,height),(24,28,34))
    draw=ImageDraw.Draw(canvas)
    draw.text((gap,12),title,font=font,fill="white")
    for i,(case,label) in enumerate(items):
        x=gap+(i%columns)*(image_size+gap); y=header+(i//columns)*(image_size+caption+gap)
        draw.text((x,y),label,font=small,fill="white")
        with Image.open(case["directory"]/img_name) as img:
            img=img.convert("RGBA").convert("RGB").resize((image_size,image_size),Image.Resampling.LANCZOS)
            canvas.paste(img,(x,y+caption))
    canvas.save(review/name)
    return name

sheets=[]
for batch in ("pilot-256", "pilot-128", "diagnostics-256"):
    seeds=list(dict.fromkeys(c["seed"] for c in cases if c["batch"]==batch))
    for seed in seeds:
        subset=[c for c in cases if c["batch"]==batch and c["seed"]==seed]
        columns=2 if batch=="diagnostics-256" else 3
        filename=f"{batch}-{seed}-textures.png"
        sheet([(c,c["method"]+" / "+c["complexity"]) for c in subset],filename,columns,"textures.png",500,
            f'{subset[0]["size"]} x {subset[0]["size"]} | seed {seed} | actual tile textures')
        sheets.append((batch,seed,filename))
        sheet([(c,c["method"]+" / "+c["complexity"]) for c in subset],f"{batch}-{seed}-semantic.png",columns,"semantic.png",350,
            f'{batch} | seed {seed} | native surfaces')
        # Every predetermined crop is included, plus the largest homogeneous dirt area.
        for c in subset:
            crop_items=[]
            for crop in c["texture_crops"]:
                crop_items.append((c,crop))
            canvas=Image.new("RGB",(1560,475),(24,28,34)); draw=ImageDraw.Draw(canvas)
            draw.text((12,10),f'{c["size"]} | {seed} | {c["method"]} / {c["complexity"]} | four 32 x 32 native crops',font=font,fill="white")
            for i,(entry,crop) in enumerate(crop_items):
                x=12+i*388
                draw.text((x,47),f'{crop["label"]} ({crop["x_native"]}, {crop["y_native"]})',font=small,fill="white")
                with Image.open(c["directory"]/f'crop-{crop["label"]}.png') as img:
                    canvas.paste(img.convert("RGBA").convert("RGB").resize((376,376),Image.Resampling.LANCZOS),(x,80))
            canvas.save(review/f'{c["name"]}-crops.png')

rows=[]
for c in cases:
    m=c["metrics"]; a=c["analysis"]
    rows.append(dict(batch=c["batch"],seed=c["seed"],size=c["size"],method=c["method"],complexity=c["complexity"],
        water_percent=m["water_percent_map"],gravel_percent_land=m["gravel_percent_land"],moss_percent_land=m["moss_percent_land"],
        largest_dirt_square=m["largest_all_dirt_square_native"],all_dirt_32_percent=m["windows"][1]["all_dirt_percent"],
        surface_edge_percent=m["surface_edges_per_100_native_edges"],dirt_boundary_distance_p90=a["boundary_distance"]["dirt_p90"],
        transect_changes=a["mean_transect_surface_changes"],dry_components=m["dry_components"],water_components=m["water_components"],
        changed_percent=100*c["intent_changed_native_cells"]/(c["size"]**2),
        terrain_ms=c["construction_ms"]+c["materialization_ms"],package_ms=c["package_and_validation_ms"],
        render_ms=c["artifact_render_ms"],semantic_sha256=c["semantic_sha256"]))
with (review/"measurements.csv").open("w",newline="") as f:
    writer=csv.DictWriter(f,rows[0]);writer.writeheader();writer.writerows(rows)
summary=[]
for size in (256,128):
    for method in ("Fields","Regions"):
        for complexity in ("Low","Standard","High"):
            group=[r for r in rows if r["batch"].startswith("pilot") and r["size"]==size and r["method"]==method and r["complexity"]==complexity]
            summary.append(dict(size=size,method=method,complexity=complexity,
                mean_largest_dirt_square=mean(r["largest_dirt_square"] for r in group),
                mean_all_dirt_32_percent=mean(r["all_dirt_32_percent"] for r in group),
                mean_surface_edge_percent=mean(r["surface_edge_percent"] for r in group),
                mean_transect_changes=mean(r["transect_changes"] for r in group)))
(review/"summary.json").write_text(json.dumps(dict(paired_means=summary,
    water_range=[min(r["water_percent"] for r in rows),max(r["water_percent"] for r in rows)],
    gravel_range=[min(r["gravel_percent_land"] for r in rows),max(r["gravel_percent_land"] for r in rows)],
    moss_range=[min(r["moss_percent_land"] for r in rows),max(r["moss_percent_land"] for r in rows)],
    changed_range=[min(r["changed_percent"] for r in rows),max(r["changed_percent"] for r in rows)],
    terrain_ms_median=median(r["terrain_ms"] for r in rows),terrain_ms_max=max(r["terrain_ms"] for r in rows)),indent=2)+"\n")
lines=["# Natural terrain comparison — design evidence",
    "", "28 retained cases: 24 paired cases at two sizes, plus four reported-seed diagnostics. These are terrain-only packages, without starts, colonies, or doodads. They are not playable-map acceptance results.",
    "", "All terrain packages were reloaded and checked cell by cell. Cross-map accessibility is not required. Texture sheets use actual NORMAL tile frames and palette; they are offline renders, not engine viewport captures.",
    "", "[Measurements](measurements.csv) · [Paired summary](summary.json)",
    "", "The scale values 112 / 64 / 36 native cells are initial calibration values. Read the same-seed Low / Standard / High columns together. Quantity settings stay at 20% water of map, 14% gravel and 8% moss of land; transitions cause small recorded deviations.",
    "", "Every case links its fixed north-west, center and south-east crops and the center of its largest all-dirt square. Each original crop is 32 x 32 native cells at 24 texture pixels per cell.",
    ""]
for batch,seed,filename in sheets:
    lines += [f"## {batch} · seed {seed}","",f"![Actual tile textures]({filename})","",
        f"[Native surface sheet]({batch}-{seed}-semantic.png)","",
        "| Method | Complexity | Largest dirt square | All-dirt 32² windows | Texture crops | Raw case |",
        "| --- | --- | ---: | ---: | --- | --- |"]
    for c in cases:
        if c["batch"]!=batch or c["seed"]!=seed: continue
        m=c["metrics"]
        lines.append(f'| {c["method"]} | {c["complexity"]} | {m["largest_all_dirt_square_native"]} | {m["windows"][1]["all_dirt_percent"]:.2f}% | [Four crops]({c["name"]}-crops.png) | [Report](../{batch}/{c["name"]}/report.json) |')
    lines.append("")
(review/"review.md").write_text("\n".join(lines)+"\n",encoding="utf-8")
print(json.dumps(json.loads((review/"summary.json").read_text(encoding="utf-8")),indent=2))
print(f"Review: {review/'review.md'}")



# Stored V10 packages remain unchanged; use the same texture renderer for reference.
import io, zipfile
preview_canvas = Image.new("RGB", (1140, 650), (24, 28, 34))
preview_draw = ImageDraw.Draw(preview_canvas)
preview_draw.text((16, 12), "Neutral-marker layout from saved maps — offline illustration, not a UI screenshot", font=font, fill="white")
for k, seed in enumerate(("265249412814339965", "464831658165234256")):
    reference = root / f"reference-{seed}"
    if not (reference/"reference.json").exists(): continue
    info = json.loads((reference/"reference.json").read_text(encoding="utf-8"))
    compare = [{"directory": reference}] + [c for c in cases if c["batch"]=="diagnostics-256" and c["seed"]==seed]
    sheet(list(zip(compare, ("Stored V10", "Fields / Standard", "Regions / Standard"))),
        f"reference-{seed}-comparison.png", 3, "textures.png", 500, f"256 x 256 | seed {seed} | stored V10 and new terrain")
    with zipfile.ZipFile(info["source"]) as package:
        with Image.open(io.BytesIO(package.read("map.png"))) as src:
            thumb = src.convert("RGBA").convert("RGB").resize((172,172),Image.Resampling.LANCZOS)
    draw=ImageDraw.Draw(thumb)
    def pos(cell):
        return (int(172*(cell["x"]-2)/info["size"]) + (cell["y"]&1), int(172*(cell["y"]-2)/info["size"]))
    for i, cell in enumerate(info["player_starts"]):
        x,y=pos(cell)
        draw.ellipse((x-8,y-8,x+8,y+8),fill=(25,25,25),outline="white")
        draw.text((x,y),chr(65+i),font=ImageFont.truetype(font_path,11),fill="white",anchor="mm")
    for cell in info["neutral_preview_positions"]:
        x,y=pos(cell)
        draw.rectangle((x-3,y-3,x+3,y+3),fill="black")
        draw.rectangle((x-2,y-2,x+2,y+2),fill=(160,160,160))
    thumb.save(reference/"preview-layout-illustration.png")
    x=16+k*566
    preview_draw.text((x,52),f'{len(info["neutral_preview_positions"])} neutral colonies / 4 starts',font=font,fill="white")
    preview_draw.text((x,86),f'Seed {seed} | enlarged 3x',font=small,fill=(185,192,201))
    preview_canvas.paste(thumb.resize((516,516),Image.Resampling.NEAREST),(x,117))
preview_canvas.save(review/"neutral-preview-illustration.png")
with (review/"review.md").open("a",encoding="utf-8") as f:
    f.write("\n## Stored V10 references and colony-preview evidence\n\n")
    for seed in ("265249412814339965","464831658165234256"):
        f.write(f"![Stored V10 alongside A and B](reference-{seed}-comparison.png)\n\n")
        f.write(f"[Read-only reference and actual marker positions](../reference-{seed}/reference.json)\n\n")
    f.write("![Offline marker illustration](neutral-preview-illustration.png)\n\n")
    f.write("Grey squares use the implemented 7-pixel bordered marker dimensions. Player circles/letters here are illustrative; the actual widget retains the engine's existing player-spawn rendering and input handling. Live map switching, hover and spawn-click checks remain for integration.\n")
