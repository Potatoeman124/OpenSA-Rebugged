# Phase 9.0 V7/V8 preservation baseline

## 1. Checkpoint identity

This document freezes the accepted Random Map Generator state before any Natural Landscape V9 reference study or implementation.

- Branch: `codex/rmg-phase-8a-parameterized-battlefield`
- Pre-task HEAD: `4d5985d9111cc2af79a90b91df0b69b81125c6a3`
- Checkpoint commit: the single commit containing this document. Resolve it with `git rev-parse HEAD`; the exact SHA is printed in the Step 0 completion response. A commit cannot contain its own final SHA because changing this file would change that SHA.
- Pinned OpenRA engine: `386f691c2e1f469596ef6f58e258a10a176bdc3d`
- Baseline date: 2026-08-30

| Player-facing family | Generator | Profile | Preservation status |
| --- | ---: | --- | --- |
| Artificial Battlefield | V7 | `normal-parameterized-battlefield-v7` | Functional frozen compatibility path |
| Structured Competitive | V8 | `normal-coherent-water-v8` | Functional frozen baseline |
| Natural Landscape | none | none | Planned, disabled, and generation rejects |

## 2. Preflight reconciliation

The repository was still at the audit's `4d5985d` base commit. The corrected V7/V8 implementation and naming work existed as one uncommitted RMG prototype; no later commit superseded the audit snapshot.

The pre-task inventory was classified as follows:

- Required V7/V8 source: RMG models, player-settings resolution, generation dispatch, blocking topology, land-cover materialization, OpenRA map adapter, coherent-Water stage, utility commands, and lobby logic.
- Required profiles and UI: `normal-parameterized-battlefield-v7.yaml`, `normal-coherent-water-v8.yaml`, and the RMG lobby controls.
- Required wrappers and history: Phase 8A/8C corpus scripts, player and low-level wrappers, fuzz/audit wrappers, Phase 8 documents, and the RMG README/parameter contract.
- Preservation additions: this report, the machine-readable fixture, and `Invoke-RmgV7V8Preservation.ps1`.
- Generated/ignored evidence: `.oramap` packages, reports, settings, previews, audits, and the external-chat handoff ZIP under `artifacts/rmg/`.
- Unrelated user work: none found.
- Uncertain files: none.

No reset, discard, merge, push, engine update, or proprietary-asset import was performed.

## 3. Current supported families

### Artificial Battlefield V7

V7 is the exact-symmetry engineered battlefield family. It preserves the strategic scaffold, player/start reservations, parameterized distributed tactical Water, movement-terrain placement, adaptive neutral-colony capacity handling, and existing deterministic streams.

### Structured Competitive V8

V8 preserves the same exact-symmetry, route-first competitive scaffold while replacing distributed tactical pools with coherent Water morphology. It is not a Natural Landscape implementation.

### Natural Landscape

Natural Landscape has no generator profile or implementation. The UI may expose it as planned, but schema-version-3 resolution throws a clear error. The preservation gate verifies that selecting it produces neither a V7 nor V8 fallback package.

## 4. Preservation corpus

The authoritative machine-readable fixture is [`regression/v7_v8_preservation_baseline.json`](regression/v7_v8_preservation_baseline.json). Raw ZIP bytes are deliberately excluded from the determinism contract because archive metadata can vary. The five stable identities are logical terrain, actors, strategic graph, canonical `map.yaml` plus `map.bin`, and OpenRA UID.

| Family | Seed | Players | Symmetry | Battlefield plan | Water | Logical SHA-256 | Actor SHA-256 | Graph SHA-256 | Canonical map SHA-256 | OpenRA UID | Adaptation |
| --- | ---: | ---: | --- | --- | --- | --- | --- | --- | --- | --- | --- || Artificial Battlefield V7 | 8300001 | 2 | horizontal | open-fields | standard | 90268d4498c1e1cf28aedf3e270fbe304e1bf25bb9468dd66ecaebd39c931578 | 0361f5a75906dce78af2b8d69d22d7b118ccfa400a1de5eda102e6d6f22d9d58 | 1053aabe1bbb9887af730378c9100313b75e2acaa6337b53f87da088d4916dae | dd874800f2b83a9b02a485434fe0ca7b1569b6d3b0dee2f8909cf15e3a1e1902 | 647feb691f8aec353802224af9adfeff5cde4958 | none |
| Structured Competitive V8 | 8300001 | 2 | horizontal | open-fields | standard | 9124d35c12fb9fab8b2414295de1facc1bf0c2d5453926158aad9822692bd05b | e0b027c457826398fc23fb4ee524fa3155c00704ea1a6dc269c8e0530b581500 | 1053aabe1bbb9887af730378c9100313b75e2acaa6337b53f87da088d4916dae | efffcef4bad475936aa56dc658913e674afbfaecce6ed50320755e5372c9ee00 | d8943bbf83f93322e991d6bc86c71ccbd47d8c01 | neutral-colonies-reduced:4/10 |
| Artificial Battlefield V7 | 8300002 | 2 | vertical | contested-center | high | 2d9a1786ce48232b67af4c4ff7cda445367358f40f9f88e9ad79fd78491255d2 | fe5ffad158dfca518213e95c2cf13d96cbddd631caccd3f4db9277fa9650720f | a2b4db28eba0039a04ec52e90ce3dbec00ee5663aa347977b09bccf6d88aa647 | 77434c3f4e7824f2921ba35516d158c65bcb1d890407d02f134ec04ef7dba1a2 | fd1cef33b73892649313c019028950be4e3a3677 | none |
| Structured Competitive V8 | 8300002 | 2 | vertical | contested-center | high | e2b11f6b1c91158786b599fd74de798cd9495563e2f84529de6de4a83019ebec | f017815d35d4969b6c4ffa58d59afcd58c9f53be2ce9313404a657b4c61d3053 | a2b4db28eba0039a04ec52e90ce3dbec00ee5663aa347977b09bccf6d88aa647 | b0d63cd334a23f68f93b8e1c97a1e54098ce98e82a7a8b8d2342055ded08a9d3 | 58d18efd2a11a98f623b72b60f5bd61036645c93 | neutral-colonies-reduced:4/10 |
| Artificial Battlefield V7 | 8300003 | 2 | rotational | contested-center | low | 812ec45d548017a2e543ad784bae35ce90dcb3f4972cda4016092f7e5984bc03 | 51ad7663a8a0a00a287995a060e2e7a9e65468c772425aaf0453df43652a75c6 | 0624077e2047de6f2317889bc83faed5004c3796e7be7b2f38450f9d403da758 | f64e536c986339ba6d5a02679a9b3265517baaa48751c9eaf33edf7370836fe6 | 9c1e37c1832e976ddf0abe90ba5c9ad50e7d76e1 | none |
| Structured Competitive V8 | 8300003 | 2 | rotational | contested-center | low | a2a5b9aa729945a8c5e226ad25337fdc0f1b1423727dd7383920c2bd190094a4 | a645002e091ef86a33e5d3fcf7a4f0dcf974e8f6a93e8f8d6f100dab40fcd732 | 0624077e2047de6f2317889bc83faed5004c3796e7be7b2f38450f9d403da758 | 6c545fe6209e79f180675d83280bf682979da9e559bb6f763c60148324de7fd5 | 4129fdb68225a4280297fa2e461ef84412bbe3a2 | neutral-colonies-reduced:4/10 |
| Artificial Battlefield V7 | 5722426127237601134 | 4 | rotational | contested-center | high | 57302668c51e828fa8f6d0d78e42a4666a95688cbe44f633be1a2ecec16faaac | db650ec559dac3b5ae7e31c827ce8ee49abd171a11d52b373be498a8abcfe48d | 9263e330dce8795ce843a4ca72a8c208424c5bfa0ffec6e5e3c6526344a03813 | 4cebc370eeeebcf4a5f305275321c046ebd921fc9e95bb0ebb811381c90478a2 | bddd6222fcfb2de949b22f80269cb3bed77d0147 | neutral-colonies-reduced:12/16 |
| Structured Competitive V8 | 5722426127237601134 | 4 | rotational | contested-center | high | ada081cc46b1ddae24501d6286e063ef79cede8b1d1f4837d74fd7050258269d | 54002771ca800dad8061ee80d2fc1fc5cc885f0deac2a10ce445376ea5d98934 | 9263e330dce8795ce843a4ca72a8c208424c5bfa0ffec6e5e3c6526344a03813 | ba6fce7d9ee96beeb26c78aad8a7603ef3a4f47bfa67e1d69bb5a55ecc4516e8 | 4797319483c933848b95d28927b7362dd46deacb | neutral-colonies-reduced:8/16 |
| Artificial Battlefield V7 | 8300005 | 4 | vertical | contested-center | standard | 847f4ce214be4a065e59eac65b6d9691a32d5dd260847d24a9a1525d2a8ae1a3 | ece74b9b83844e57d9351962cfe4cdf517d3b3a4deee2b2f74ebe1e00d64d7a3 | dd3eca6a19253a7dc2a58fcb33624b4d649828c0a5f0977e44789bdc6be3f70e | 54d480ce18a0b4fea9ba072e6e87904565c70d14fffa9164ba3faedab6b711f7 | 92307a46824edf01e064ca96fc65a4053d20ff54 | none |
| Structured Competitive V8 | 8300005 | 4 | vertical | contested-center | standard | 056ad87b40f2887c6c85f34a6f029f8fb02d0da45a1c8f5486f4cf167a7c9389 | f9a73212979168bd938a6c853b5439fdbc0986d5e9e97891e4053908e30a46af | dd3eca6a19253a7dc2a58fcb33624b4d649828c0a5f0977e44789bdc6be3f70e | 79df37b15b3bcb157307b16087f455f0a4a3443a0b4fdadc0eb70c8e8915cb62 | 0e28e3f83798f35e4b73be644baa615087e65ffc | neutral-colonies-reduced:8/16 |
| Artificial Battlefield V7 | 8300006 | 4 | rotational | open-fields | standard | 69a15477881bab64c8f15bbb2025b6f82afc5188551d01bbeb42a253420f9a50 | 9e33e88069747a77ae65f1d3c3cadfda70a0f323c7a5570900e84cac1f818984 | 01ed883a0a38cf9d3c0aef0ebcf13d822dcce45d71ee3f03df4e91881e063abe | 89f9f82848b61a6859460540b92027ebdb418056db853d01c2eff9e59c8f935d | 95d4f6ce1a4f2fd60c0f16921d963324329ac175 | none |
| Structured Competitive V8 | 8300006 | 4 | rotational | open-fields | standard | e2941c416d9d4a863653626a1f41da447560aa9cd88f0ebc5c000436ca29e5ac | 1c54556b7ea25bd015968fb9a0f9127be6b11636e42230c29dc47fe1d308d660 | 01ed883a0a38cf9d3c0aef0ebcf13d822dcce45d71ee3f03df4e91881e063abe | 50721e90ede0c83f410bba257e2d0fc2742cbff02de002492c49b1aab3b89013 | 4f2460b841a2018324e8aa212660bc4f79a21d1f | neutral-colonies-reduced:8/16 |

Every case passed logical validation, native movement validation, package reload, rules/sequences initialization, and map-YAML lint. The fixture also freezes normalized schema-version-3 settings, profile identity, requested/achieved neutral-colony counts, warnings, and adaptations.

## 5. Regression commands

Run from the repository root in Windows PowerShell:

```powershell
./build-pipeline.cmd build
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/rmg/Invoke-RmgSelfTests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/rmg/Prepare-Phase8cLayoutFamilyCorpus.ps1 -Overwrite
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/rmg/Invoke-RmgV7V8Preservation.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/rmg/Invoke-BattlefieldLayoutAudit.ps1 `
    -OutputDirectory artifacts/rmg/v7-v8-preservation/audit `
    -GeneratedMapDirectory artifacts/rmg/phase-8c-structured-competitive/examples `
    -GeneratedMapPattern OpenSA-RMG-*.oramap
./build-pipeline.cmd validate
git diff --check
```

`Invoke-RmgV7V8Preservation.ps1` generates each of the 12 cases in two independent PowerShell/utility processes, compares all five identities between processes and against the fixture, verifies normalized settings/profile/family/colony adaptation, and asserts Natural Landscape rejection.

## 6. Compatibility notes

- Historical V8 deterministic stream identifiers containing `natural-water-*` are retained. They are internal compatibility identifiers, not a player-facing classification, and renaming them would change V8 output.
- Schema versions 1 and 2 retain their prior compatibility behavior. Schema version 3 performs the corrected family resolution.
- V7 and V8 use distinct player slugs, generator versions, and profiles. Natural Landscape does not alias either path.
- Safe neutral-colony reduction is part of the frozen behavior when the requested density cannot fit without violating protected space or validation gates.

## 7. Known limitations intentionally preserved

These are baseline properties, not Step 0 defects to repair:

- exact geometric symmetry;
- route-first competitive construction;
- a 64 x 64 logical macro-grid materialized to 128 x 128 native cells;
- NORMAL tileset and 128 x 128 output;
- 2-player and 4-player player-facing configurations;
- Open Fields and Contested Center battlefield plans;
- sparse decoration relative to many authored maps;
- no elevation, hydrology, drainage, biome simulation, erosion, terrain-first routing, or organic coast/river model;
- static native-movement/package validation rather than a constructible live `World` simulation.

## 8. Natural V9 boundary

Future Natural Landscape work must be a separate generator family and version. It must not silently alter V7/V8 identities, redirect the Natural Landscape option to either existing family, or relabel coherent Water as natural generation. Any future terrain-first algorithm requires a new contract, its own profiles and deterministic streams, separate regression fixtures, full authored-map comparison, and human visual acceptance.

## 9. Validation evidence

- Release build: passed with zero warnings and zero errors.
- RMG self-tests: V1 through V8, inherited baselines, parameter matrices, terrain catalogues, materializers, and native validator passed.
- Preservation fixture: 12/12 cases matched across two independent processes; Natural Landscape rejection passed.
- Battlefield-layout audit: 125 shipped maps plus 12 generated comparisons completed (`campaign=100`, `custom=13`, `skirmish=11`, `other=1`).
- Full validation: Release and Debug builds, MiniYAML, rules, sequences, translations, all shipped maps, and 113 Lua scripts passed.
- Existing non-RMG diagnostics retained: three IDE0074 suggestions, two unused translation-attribute warnings, and the pre-existing asset-policy inventory findings. No new validation error was introduced.
- Proprietary/generated map packages and previews remain ignored and are not part of the checkpoint commit.

The baseline acceptance gate is satisfied by the commit containing this report:

`V7/V8 BASELINE FROZEN - READY FOR NATURAL V9 REFERENCE STUDY`