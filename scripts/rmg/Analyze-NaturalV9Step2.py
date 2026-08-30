#!/usr/bin/env python3
"""Render and analyze the Natural V9 Step 2 terrain-only A/B corpus.

This script intentionally reports independent diagnostic axes. It does not
compute an aggregate naturalness score and it does not perform gameplay,
route, fairness, decoration, or map-package analysis.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import importlib
import json
import math
import random
import statistics
import sys
import time
from collections import defaultdict
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

WIDTH = HEIGHT = 128
REVIEW_SEED = 92091
VARIANTS = {
    "variant-a": "correlated-field-baseline",
    "variant-b": "correlated-field-with-basin-potential",
}
PALETTE = np.array([
    (134, 95, 69),
    (69, 95, 134),
    (118, 118, 118),
    (69, 134, 75),
], dtype=np.uint8)
FIELD_RANGES = {
    "landform-raw": (-1.5, 1.5),
    "landform-normalized": (0.0, 1.0),
    "domain-warp-x": (-5.5, 5.5),
    "domain-warp-y": (-5.5, 5.5),
    "basin-potential": (-1.5, 0.0),
    "moisture": (-1.0, 1.0),
    "roughness": (-1.0, 1.0),
    "water-decision": (-0.5, 0.5),
    "rock-decision": (-0.75, 0.75),
    "vegetation-decision": (-0.75, 0.75),
}
WORST_AXES = [
    ("water_straight_run_share", True),
    ("water_longest_boundary_run_norm", True),
    ("water_micro_area_share", True),
    ("water_edge_share", True),
    ("water_components", True),
    ("water_largest_share", False),
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def load_step1_modules(step1: Path):
    source = step1 / "analysis-source"
    if not source.is_dir():
        raise RuntimeError(f"Step 1 analysis source is missing: {source}")
    sys.path.insert(0, str(source))
    style_metrics = importlib.import_module("style_metrics")
    semantic_maps = importlib.import_module("semantic_maps")
    return style_metrics, semantic_maps


def iter_candidates(candidate_root: Path):
    for variant_dir, variant_id in VARIANTS.items():
        for root_seed in (92001, 92002, 92003):
            for index in range(8):
                path = candidate_root / variant_dir / f"root-{root_seed}" / f"candidate-{index:02d}"
                if not path.is_dir():
                    raise RuntimeError(f"Missing candidate: {path}")
                yield variant_dir, variant_id, root_seed, index, path


def load_candidate(path: Path):
    manifest = json.loads((path / "candidate-manifest.json").read_text(encoding="utf-8-sig"))
    semantic = np.fromfile(path / "semantic" / "semantic.u8", dtype=np.uint8)
    if semantic.size != WIDTH * HEIGHT:
        raise RuntimeError(f"Invalid semantic size in {path}: {semantic.size}")
    semantic = semantic.reshape((HEIGHT, WIDTH))
    fields = {}
    for field in FIELD_RANGES:
        data = np.fromfile(path / "fields" / f"{field}.f32", dtype="<f4")
        if data.size != WIDTH * HEIGHT:
            raise RuntimeError(f"Invalid field size for {field} in {path}: {data.size}")
        fields[field] = data.reshape((HEIGHT, WIDTH))
    return manifest, semantic, fields


def grayscale(field: np.ndarray, low: float, high: float) -> np.ndarray:
    normalized = np.clip((field - low) / (high - low), 0.0, 1.0)
    return np.round(normalized * 255).astype(np.uint8)


def render_debug(path: Path, semantic: np.ndarray, fields: dict, semantic_maps) -> dict:
    debug = path / "debug"
    semantic_dir = path / "semantic"
    debug.mkdir(parents=True, exist_ok=True)
    semantic_dir.mkdir(parents=True, exist_ok=True)
    hashes = {}
    for name, field in fields.items():
        output = debug / f"{name}.png"
        Image.fromarray(grayscale(field, *FIELD_RANGES[name]), mode="L").save(output, optimize=True)
        hashes[f"debug/{output.name}"] = sha256(output)

    semantic_path = semantic_dir / "semantic.png"
    Image.fromarray(PALETTE[semantic], mode="RGB").save(semantic_path, optimize=True)
    hashes["semantic/semantic.png"] = sha256(semantic_path)

    water = semantic == 1
    components = semantic_maps.components(water)
    labels = np.zeros((HEIGHT, WIDTH), dtype=np.uint16)
    # Reconstruct component labels with a deterministic flood fill.
    next_label = 1
    seen = np.zeros_like(water, dtype=bool)
    for y, x in zip(*np.nonzero(water)):
        if seen[y, x]:
            continue
        stack = [(int(y), int(x))]
        seen[y, x] = True
        while stack:
            cy, cx = stack.pop()
            labels[cy, cx] = next_label
            for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                ny, nx = cy + dy, cx + dx
                if 0 <= ny < HEIGHT and 0 <= nx < WIDTH and water[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    stack.append((ny, nx))
        next_label += 1

    colors = np.zeros((HEIGHT, WIDTH, 3), dtype=np.uint8)
    colors[:] = (25, 25, 25)
    for label in range(1, next_label):
        value = (label * 0x9E3779B1) & 0xFFFFFFFF
        colors[labels == label] = (64 + value % 160, 64 + (value >> 8) % 160, 64 + (value >> 16) % 160)
    component_path = debug / "water-component-labels.png"
    Image.fromarray(colors, mode="RGB").save(component_path, optimize=True)
    hashes["debug/water-component-labels.png"] = sha256(component_path)

    boundary = semantic_maps.boundary_mask(water)
    boundary_image = PALETTE[semantic].copy()
    boundary_image[boundary] = (255, 220, 40)
    boundary_path = debug / "water-boundary-overlay.png"
    Image.fromarray(boundary_image, mode="RGB").save(boundary_path, optimize=True)
    hashes["debug/water-boundary-overlay.png"] = sha256(boundary_path)
    return {"hashes": hashes, "water_component_count": len(components)}


def candidate_metrics(style_metrics, semantic: np.ndarray, variant_id: str, root_seed: int, index: int, path: Path):
    identity = f"{variant_id}/root-{root_seed}/candidate-{index:02d}"
    metadata = {
        "id": identity,
        "title": identity,
        "tileset": "NORMAL",
        "playable_size": [WIDTH, HEIGHT],
        "package": str(path),
    }
    measured = style_metrics.measure(semantic, metadata)
    measured["prototype_id"] = "natural-v9-terrain-prototype-step2"
    measured["variant"] = variant_id
    measured["root_seed"] = str(root_seed)
    measured["candidate_index"] = index
    measured["decoration_metrics"] = "NOT_APPLICABLE"
    measured["gameplay_route_fairness_metrics"] = "NOT_APPLICABLE"
    measured["post_hoc_cleanup"] = False
    measured["cleanup_changed_cells"] = 0
    flat = style_metrics.flatten(measured)
    flat.update({
        "variant": variant_id,
        "root_seed": str(root_seed),
        "candidate_index": index,
        "decoration_metrics": "NOT_APPLICABLE",
        "gameplay_route_fairness_metrics": "NOT_APPLICABLE",
    })
    return measured, flat


def scalar_summary(rows: list[dict], fields: list[str]) -> dict:
    output = {"count": len(rows), "axes": {}}
    for field in fields:
        values = [float(row[field]) for row in rows if row.get(field) not in (None, "")]
        output["axes"][field] = None if not values else {
            "min": min(values),
            "median": statistics.median(values),
            "mean": statistics.fmean(values),
            "max": max(values),
        }
    return output


def diagnostic_ranks(rows: list[dict]) -> tuple[dict, dict]:
    ranks = {}
    selected = {}
    for variant_id in VARIANTS.values():
        variant_rows = [row for row in rows if row["variant"] == variant_id]
        ranked_axes = {}
        ordered_union = []
        roles = defaultdict(list)
        for axis, descending in WORST_AXES:
            ordered = sorted(variant_rows, key=lambda row: float(row[axis]), reverse=descending)
            entries = []
            for rank, row in enumerate(ordered, 1):
                identity = row["id"]
                entries.append({"rank": rank, "id": identity, "value": row[axis]})
                if rank <= 3:
                    roles[identity].append(f"{axis}:{'high' if descending else 'low'}:rank-{rank}")
                    ordered_union.append(identity)
            ranked_axes[axis] = {
                "direction": "high" if descending else "low",
                "entries": entries,
            }

        distinct = []
        for identity in ordered_union:
            if identity not in distinct:
                distinct.append(identity)
            if len(distinct) == 3:
                break
        ranks[variant_id] = ranked_axes
        selected[variant_id] = [{"id": identity, "roles": roles[identity]} for identity in distinct]
    return ranks, selected


def sample_random(rows: list[dict]) -> dict:
    rng = random.Random(REVIEW_SEED)
    selected = {}
    for variant_id in VARIANTS.values():
        result = []
        for root_seed in ("92001", "92002", "92003"):
            group = [row for row in rows if row["variant"] == variant_id and row["root_seed"] == root_seed]
            result.extend(rng.sample(group, 4))
        selected[variant_id] = [{"id": row["id"], "roles": ["deterministic-random"]} for row in result]
    return selected


def merge_selection(random_selected: dict, worst_selected: dict) -> list[dict]:
    merged = {}
    for variant_id in VARIANTS.values():
        for source in (random_selected[variant_id], worst_selected[variant_id]):
            for item in source:
                entry = merged.setdefault(item["id"], {
                    "id": item["id"],
                    "variant": variant_id,
                    "label": f"{'A' if variant_id == VARIANTS['variant-a'] else 'B'} | {item['id'].split('/')[1]} | {item['id'].split('/')[2]}",
                    "roles": [],
                })
                for role in item["roles"]:
                    if role not in entry["roles"]:
                        entry["roles"].append(role)
    items = list(merged.values())
    random.Random(REVIEW_SEED).shuffle(items)
    for index, item in enumerate(items, 1):
        item["blind_code"] = f"G{index:02d}"
    return items


def letterbox(source: Image.Image, width: int, height: int, background=(12, 12, 12)) -> Image.Image:
    image = source.convert("RGB").copy()
    scale = min(width / image.width, height / image.height)
    image = image.resize((max(1, round(image.width * scale)), max(1, round(image.height * scale))), Image.Resampling.NEAREST)
    output = Image.new("RGB", (width, height), background)
    output.paste(image, ((width - image.width) // 2, (height - image.height) // 2))
    return output


def render_sheet(items: list[dict], image_lookup: dict[str, Path], output: Path, title: str, blind: bool) -> str:
    cols = 4
    cell_width, cell_height = 280, 302
    header = 42
    rows = math.ceil(len(items) / cols)
    canvas = Image.new("RGB", (cols * cell_width, header + rows * cell_height), (8, 8, 8))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default()
    draw.text((12, 10), title, fill=(245, 245, 245), font=font)
    for i, item in enumerate(items):
        x = (i % cols) * cell_width
        y = header + (i // cols) * cell_height
        preview = letterbox(Image.open(image_lookup[item["id"]]), 248, 248)
        canvas.paste(preview, (x + 16, y + 30))
        label = item["blind_code"] if blind else item.get("label", item["id"])
        draw.text((x + 8, y + 8), label[:45], fill=(245, 245, 245), font=font)
        if not blind and item.get("roles"):
            draw.text((x + 8, y + 282), ",".join(item["roles"])[:45], fill=(190, 210, 255), font=font)
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output, optimize=True)
    return sha256(output)


def calibration_items(step1: Path) -> tuple[list[dict], dict[str, Path]]:
    selection = json.loads((step1 / "analysis-source" / "reference_selection.json").read_text(encoding="utf-8"))
    primary = selection["groups"]["primary"][:8]
    negatives = selection["groups"]["negative_authored"] + selection["groups"]["negative_v7"] + selection["groups"]["negative_v8"]
    items = []
    lookup = {}
    for group, records in (("primary", primary), ("negative", negatives[:8])):
        for record in records:
            identity = f"calibration/{record['id']}"
            if group == "primary":
                path = step1 / "evidence" / "reference-previews" / f"{record['id']}.png"
            elif record["id"].startswith("OpenSA-RMG") and "structured" in record["id"]:
                path = step1 / "evidence" / "negative-controls" / "v8" / f"{record['id']}.png"
            elif record["id"].startswith("OpenSA-RMG"):
                path = step1 / "evidence" / "negative-controls" / "v7" / f"{record['id']}.png"
            else:
                path = step1 / "evidence" / "negative-controls" / "authored" / f"{record['id']}.png"
            if not path.is_file():
                raise RuntimeError(f"Calibration preview is missing: {path}")
            items.append({
                "id": identity,
                "label": record["id"],
                "calibration_group": group,
                "roles": [f"calibration-{group}"],
            })
            lookup[identity] = path

    random.Random(REVIEW_SEED ^ 0xCA11).shuffle(items)
    for index, item in enumerate(items, 1):
        item["blind_code"] = f"C{index:02d}"
    return items, lookup


def write_answer_key(output: Path, calibration: list[dict], generated: list[dict], candidate_root: Path, image_lookup: dict[str, Path]) -> None:
    records = []
    for item in calibration:
        records.append({
            "sheet": "calibration",
            "blind_code": item["blind_code"],
            "id": item["id"],
            "source_group": item.get("calibration_group", ""),
            "variant": "",
            "generator_version": "REFERENCE_OR_CONTROL",
            "normalized_settings": "",
            "root_seed": "",
            "candidate_index": "",
            "candidate_root_subseed_u64": "",
            "sampling_roles": "|".join(item.get("roles", [])),
            "metrics_path": "",
            "semantic_image_sha256": sha256(Path(image_lookup[item["id"]])),
        })

    variant_directories = {value: key for key, value in VARIANTS.items()}
    for item in generated:
        variant_id, root_part, candidate_part = item["id"].split("/")
        root_seed = root_part.removeprefix("root-")
        candidate_index = int(candidate_part.removeprefix("candidate-"))
        candidate_path = candidate_root / variant_directories[variant_id] / root_part / candidate_part
        manifest = json.loads((candidate_path / "candidate-manifest.json").read_text(encoding="utf-8-sig"))
        records.append({
            "sheet": "generated",
            "blind_code": item["blind_code"],
            "id": item["id"],
            "source_group": "",
            "variant": variant_id,
            "generator_version": "Natural V9 Step 2 terrain prototype; production version remains unavailable",
            "normalized_settings": json.dumps({
                "tileset": "NORMAL",
                "size": "128x128",
                "morphology": "NATURAL_INLAND_LAKES_V1",
                "post_hoc_cleanup": False,
            }, sort_keys=True, separators=(",", ":")),
            "root_seed": root_seed,
            "candidate_index": candidate_index,
            "candidate_root_subseed_u64": manifest["streams"]["candidate-root"]["subseed_u64"],
            "sampling_roles": "|".join(item.get("roles", [])),
            "metrics_path": str((candidate_path / "metrics.json").relative_to(output.parent)).replace("\\", "/"),
            "semantic_image_sha256": sha256(image_lookup[item["id"]]),
        })

    fields = [
        "sheet", "blind_code", "id", "source_group", "variant", "generator_version",
        "normalized_settings", "root_seed", "candidate_index", "candidate_root_subseed_u64",
        "sampling_roles", "metrics_path", "semantic_image_sha256",
    ]
    with (output / "answer-key.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        writer.writerows(records)
    write_json(output / "answer-key.json", {
        "schema_version": 1,
        "review_seed": REVIEW_SEED,
        "items": records,
    })
def write_review_template(output: Path, calibration: list[dict], generated: list[dict]) -> None:
    fields = [
        "sheet", "blind_code", "natural_style_rating", "morphology_fit",
        "defect_tags", "reviewer_notes",
    ]
    with (output / "review-template.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for sheet, items in (("calibration", calibration), ("generated", generated)):
            for item in items:
                writer.writerow({
                    "sheet": sheet,
                    "blind_code": item["blind_code"],
                    "natural_style_rating": "",
                    "morphology_fit": "",
                    "defect_tags": "",
                    "reviewer_notes": "",
                })


def reference_comparison(step1: Path, variant_summaries: dict) -> dict:
    selected_summary = json.loads((step1 / "evidence" / "metrics" / "selected_group_summary.json").read_text(encoding="utf-8"))
    anchors = json.loads((step1 / "evidence" / "metrics" / "natural_style_metrics.json").read_text(encoding="utf-8"))
    anchor_ids = {"006_Expand_And_Destroy", "007_Destroy_Enemies", "001_Colony_Defence", "038_They_are_Everywhere"}
    anchor_metrics = []
    for value in anchors.get("maps", []):
        if value.get("map", {}).get("id") in anchor_ids:
            anchor_metrics.append(value)
    return {
        "schema_version": 1,
        "morphology_id": "NATURAL_INLAND_LAKES_V1",
        "core_anchors": ["006_Expand_And_Destroy", "007_Destroy_Enemies"],
        "supporting_anchors": ["001_Colony_Defence", "038_They_are_Everywhere"],
        "selected_group_diagnostic_ranges": selected_summary,
        "anchor_metrics": anchor_metrics,
        "prototype_variant_summaries": variant_summaries,
        "interpretation": "Independent diagnostic comparison only; no aggregate score and no visual acceptance decision.",
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-root", required=True)
    parser.add_argument("--step1-handoff", required=True)
    parser.add_argument("--output-root", required=True)
    args = parser.parse_args()

    started = time.perf_counter()
    candidate_root = Path(args.candidate_root).resolve()
    step1 = Path(args.step1_handoff).resolve()
    output_root = Path(args.output_root).resolve()
    metrics_root = output_root / "metrics"
    visual_root = output_root / "visual-review"
    style_metrics, semantic_maps = load_step1_modules(step1)

    measured_records = []
    flat_rows = []
    image_lookup = {}
    render_started = time.perf_counter()
    for variant_dir, variant_id, root_seed, index, path in iter_candidates(candidate_root):
        manifest, semantic, fields = load_candidate(path)
        expected = f"{variant_id}/root-{root_seed}/candidate-{index:02d}"
        if manifest["variant"] != variant_id:
            raise RuntimeError(f"Variant mismatch in {path}")
        debug = render_debug(path, semantic, fields, semantic_maps)
        measured, flat = candidate_metrics(style_metrics, semantic, variant_id, root_seed, index, path)
        measured["candidate_manifest_sha256"] = sha256(path / "candidate-manifest.json")
        measured["debug_evidence"] = debug
        measured["semantic_sha256"] = manifest["semantic_sha256"]
        write_json(path / "metrics.json", measured)
        measured_records.append(measured)
        flat_rows.append(flat)
        image_lookup[expected] = path / "semantic" / "semantic.png"
    render_elapsed = (time.perf_counter() - render_started) * 1000

    metric_fields = sorted({key for row in flat_rows for key in row})
    metrics_root.mkdir(parents=True, exist_ok=True)
    with (metrics_root / "candidate-metrics.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=metric_fields)
        writer.writeheader()
        writer.writerows(flat_rows)
    write_json(metrics_root / "candidate-metrics.json", {
        "schema_version": 1,
        "prototype_id": "natural-v9-terrain-prototype-step2",
        "candidate_count": len(measured_records),
        "decoration_metrics": "NOT_APPLICABLE",
        "gameplay_route_fairness_metrics": "NOT_APPLICABLE",
        "maps": measured_records,
    })

    summary_fields = [
        "water_fraction", "water_components", "water_largest_share", "water_top3_share",
        "water_micro_area_share", "water_perimeter_to_area", "water_weighted_compactness",
        "water_edge_share", "water_center_share", "water_sector_coverage",
        "water_straight_run_share", "water_longest_boundary_run_norm",
        "water_horizontal_dice", "water_vertical_dice", "water_rotation_180_dice",
        "rock_fraction", "vegetation_fraction", "water_vegetation_sector_correlation_8x8",
        "water_rock_sector_correlation_8x8", "rock_vegetation_sector_correlation_8x8",
        "semantic_straight_run_share",
    ]
    variant_summaries = {
        variant_id: scalar_summary([row for row in flat_rows if row["variant"] == variant_id], summary_fields)
        for variant_id in VARIANTS.values()
    }
    root_summaries = {
        variant_id: {
            root_seed: scalar_summary([
                row for row in flat_rows
                if row["variant"] == variant_id and row["root_seed"] == root_seed
            ], summary_fields)
            for root_seed in ("92001", "92002", "92003")
        }
        for variant_id in VARIANTS.values()
    }
    write_json(metrics_root / "variant-summaries.json", variant_summaries)
    write_json(metrics_root / "root-summaries.json", root_summaries)

    ranks, worst_selected = diagnostic_ranks(flat_rows)
    write_json(metrics_root / "worst-case-diagnostic-ranks.json", {
        "selection_basis": "Union of independent diagnostic extremes; no aggregate score.",
        "axes": ranks,
        "selected": worst_selected,
    })
    random_selected = sample_random(flat_rows)
    generated_items = merge_selection(random_selected, worst_selected)
    write_json(visual_root / "review-selection.json", {
        "schema_version": 1,
        "review_seed": REVIEW_SEED,
        "random_selection": random_selected,
        "worst_case_selection": worst_selected,
        "deduplicated_blind_items": generated_items,
        "notes": [
            "Random selection covers every root seed with four candidates per root and variant.",
            "Worst-case selection is the union of independent diagnostic extremes, not an aggregate score.",
            "If a candidate has both roles it appears once and records all roles.",
        ],
    })

    calibration, calibration_lookup = calibration_items(step1)
    calibration_blind = render_sheet(calibration, calibration_lookup, visual_root / "calibration-blind.png", "Step 2 calibration pack", True)
    calibration_labeled = render_sheet(calibration, calibration_lookup, visual_root / "calibration-labeled.png", "Step 2 calibration pack - answer labels", False)
    generated_blind = render_sheet(generated_items, image_lookup, visual_root / "candidates-blind.png", "Step 2 generated A/B blind pack", True)
    generated_labeled = render_sheet(generated_items, image_lookup, visual_root / "candidates-labeled.png", "Step 2 generated A/B pack - answer labels", False)

    repeat_path = output_root / "working" / "candidates-blind-repeat.png"
    repeat_hash = render_sheet(generated_items, image_lookup, repeat_path, "Step 2 generated A/B blind pack", True)
    if repeat_hash != generated_blind:
        raise RuntimeError("Deterministic blind-sheet rerender hash mismatch.")

    write_answer_key(visual_root, calibration, generated_items, candidate_root, {**image_lookup, **calibration_lookup})
    write_review_template(visual_root, calibration, generated_items)
    write_json(visual_root / "sheet-determinism.json", {
        "review_seed": REVIEW_SEED,
        "calibration_blind_sha256": calibration_blind,
        "calibration_labeled_sha256": calibration_labeled,
        "candidates_blind_sha256": generated_blind,
        "candidates_labeled_sha256": generated_labeled,
        "candidates_blind_repeat_sha256": repeat_hash,
        "repeat_identical": repeat_hash == generated_blind,
    })

    write_json(metrics_root / "reference-comparison.json", reference_comparison(step1, variant_summaries))
    analysis_elapsed = (time.perf_counter() - started) * 1000
    write_json(metrics_root / "analysis-performance.json", {
        "schema_version": 1,
        "candidate_count": len(flat_rows),
        "render_and_candidate_analysis_ms": render_elapsed,
        "total_analysis_and_review_export_ms": analysis_elapsed,
        "mean_ms_per_candidate": analysis_elapsed / len(flat_rows),
    })

    print(json.dumps({
        "candidate_count": len(flat_rows),
        "generated_blind_item_count": len(generated_items),
        "calibration_item_count": len(calibration),
        "candidates_blind_sha256": generated_blind,
        "repeat_identical": repeat_hash == generated_blind,
        "analysis_ms": analysis_elapsed,
    }, indent=2))


if __name__ == "__main__":
    main()
