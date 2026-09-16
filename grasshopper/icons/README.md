# OpenPyStruct icon set

Thirteen glyphs, one drawing language, authored as code. Adapted from the Eddy3D v4 icon system
(https://github.com/Eddy3D-Dev/Eddy3D, `GUI/Icons/icons/v4/`), so the rules below are theirs; what
is ours is the Structure family and the structural motifs.

## Contents

```
src/ops-vec.js       the engine: drawing primitives, motif library (M), badges (B), palette (FAM)
src/ops-vec-set.js   one def() per component — the set
src/emit.js          the exporter; regenerates all four artefacts below in one pass
svg/                 one standalone 24x24 SVG per glyph
png24/               the same rasterised at 24x24 with transparency — what the .gha embeds
OpenPyStruct_Icons_Vector.svg   sprite sheet, one <symbol id="ops-NAME"> per glyph
manifest.csv         name, ribbon tab, family, accent
```

## The rules

- **Space.** 24 x 24 units, art inside 0.7..23.3. The guard fails the run rather than cropping.
- **Stroke.** Two weights only: 1.25 for contours, 0.85 for interior hairlines. Round caps, `#14181c`.
- **Fill.** White for built volume with grey shaded faces; the family accent fills what carries load.
- **Effects.** None.
- **Composition.** One primary motif plus at most one badge, bottom-right at (17, 17) r 4.4.
  Sparkles are the exception, top-left.
- **Hue means physics, not ribbon position.** Three families are in use here:

  | Family | Accent | Why |
  |---|---|---|
  | Structure | `#b5821f` | Statics. An eighth hue for the same reason Eddy3D's Stormwater got a seventh: it is a physics none of the existing hues describes. Amber sits in the empty gap of the wheel, stays apart from Thermal's `#e4572e` at 24px, and reads as structural steel. |
  | Prediction | `#7a5af5` | Generate Data, Train Surrogate and Predict. Eddy3D's ML hue, because a trained surrogate is the same idea in both plugins. |
  | Tooling | `#6b7580` | Engine, which is container plumbing and not mechanics. |

  The other five Eddy3D families are kept in the palette so this engine can be re-synced with
  upstream, and so a future component that genuinely belongs to one can say so.

## The structural motifs

`member` is the one that matters: a beam or column drawn with its **section depth** across the
axis, because depth is what the optimizer designs (`I = b h^3 / 12`). A deeper member is a bigger
I in the icons exactly as in Visualize Result. Alongside it: `pinSupport`, `rollerSupport`,
`fixedBase`, `udl` and `bending`. A component that cannot be said with a motif plus a badge needs
a considered addition here, not a one-off drawing.

## Adding or changing a component

Write the `def()` in `src/ops-vec-set.js`, then export:

```bash
cd grasshopper/icons/src
OPENPYSTRUCT_CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" node emit.js
```

`node emit.js Beam_Model` re-exports one glyph; a full run also prunes artefacts whose `def()` is
gone. Rasterisation needs Chromium plus python3 with Pillow; without them the vectors are still
written and only the PNG step is skipped.

## The file name is the wiring

Nothing maps a component to its icon except the file name, which must equal the component's
Grasshopper display Name with spaces and `-` and `/` turned into `_`, `+` into `Plus`, punctuation
dropped and `_` runs collapsed — the transform in `OpenPyStruct.GH/Icons.cs`. A glyph one character
off is silently unused: the component keeps Grasshopper's default box and nothing errors, which is
what `TestIconCoverage` in `OpenPyStruct.Core.Tests` exists to catch.

`OpenPyStruct.png` is the exception with no component behind it: it is the ribbon tab's icon and
the assembly's.

## Licensing

The engine and exporter are Eddy3D's code under GPL-3.0-or-later, as is the plugin that embeds the
output. See `../OpenPyStruct.GH/GUI/README.md`.
