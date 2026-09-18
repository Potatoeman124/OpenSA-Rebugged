# OSArB 1.1 branding

The main-menu version label displays **OSArB 1.1: Return of the RMG**. It is configured in `mods/sa/chrome/mainmenu.yaml`; the menu logic respects that display text, falling back to the manifest version only when no label is configured. Internal package versions and map directories retain their existing packaging behavior.

`mods/sa/uibits/loading-rebugged.png` combines the existing OpenSA title and insect hexes from `loading0.png` with an image-generated reBugged wordmark underneath. The original source artwork remains in place. The additional lettering was generated to match its condensed bronze-gold, beveled style and refined after native visual review.

The shared asset is a transparent 512 x 512 texture, as required by the engine's power-of-two texture loader. The menu crops region `128, 101, 256, 310`; the startup loading screen centers the full texture. The five illustrated loading screens retain their existing selection behavior.

## Validation

On 2026-09-18, native captures exercised the actual shellmap main menu and startup logo at 1024 x 600 and 1280 x 800. Checks confirmed the exact release label, sufficient text width and separation from the logo. Both sizes were rendered successfully and the final compact menu was visually reviewed. Evidence is in `artifacts/release-branding/final-compact/` and `final-wide/`. The temporary capture helper is preserved under `artifacts/release-branding/`, outside the game assembly.
