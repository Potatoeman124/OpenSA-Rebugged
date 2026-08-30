# Phase 8C corrected layout-family manual corpus

## Purpose

This corpus compares the same settings under **Structured Competitive Version 8** and **Artificial Battlefield Version 7**. Natural Landscape is not represented because it is planned and has no implementation.

## Generation

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/rmg/Prepare-Phase8cLayoutFamilyCorpus.ps1 `
    -InstallForPlay -Overwrite
```

The wrapper creates twelve maps: each row is generated once per implemented family.

| Seed | Players | Symmetry | Battlefield plan | Water | Primary review |
| ---: | ---: | --- | --- | --- | --- |
| `8300001` | 2 | Horizontal | Open Fields | Standard | Dominant body and route readability |
| `8300002` | 2 | Vertical | Contested Center | High | Coherent Water scale in playable space |
| `8300003` | 2 | Rotational | Contested Center | Low | Avoiding micro-pool noise at small budget |
| `5722426127237601134` | 4 | Rotational | Contested Center | High | Density fallback and route safety |
| `8300005` | 4 | Vertical | Contested Center | Standard | Four-player territory and Water balance |
| `8300006` | 4 | Rotational | Open Fields | Standard | Multi-basin morphology and visible symmetry |

## Review checklist

For each Structured/Artificial pair record:

- whether Version 8 has fewer and larger Water bodies than Version 7;
- whether both still read as route-first, exact-symmetry competitive layouts;
- whether Artificial Battlefield preserves the accepted Version 7 topology;
- whether meaningful Water occupies plausible travel and combat space;
- whether starts, production exits, colonies, and main/flank routes remain usable;
- whether adaptive density warnings correspond to sensible maps;
- whether shoreline and open-Water details remain correct; and
- whether Low, Standard, and High remain perceptibly ordered.

## Acceptance boundary

Do not describe Version 8 as Natural Landscape. It can be accepted only as Structured Competitive with coherent Water. A future Natural Landscape generator needs separate terrain-first design, implementation, audit, and human acceptance.