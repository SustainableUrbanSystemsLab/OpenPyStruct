# OpenPyStruct for Grasshopper

A Rhino 8 / Grasshopper plugin around the OpenPyStruct optimizers and surrogates. Model the
structure in Rhino, press Run, and the OpenSees + PyTorch engine runs in a container; results come
back as sections sized by their moment of inertia, force diagrams and deflected shapes.

The plugin is laid out like an [Eddy3D](https://github.com/Eddy3D-Dev/Eddy3D) plugin: a ribbon of
small components — **Model → Loads → Settings → Run → Results** — that pass one object down the
wire, background runs that never block the canvas, inline run toggles, and a Rhino-free core
library with the unit tests. It owns none of the physics: every number comes from the
`openpystruct` Python package at the repository root, which factors the research scripts into
importable, tested modules.

```
Rhino/Grasshopper (C#, .gha)          container (Podman or Docker)
┌──────────────────────────┐  case.json   ┌─────────────────────────────┐
│ Beam Model / Frame Model │ ───────────▶ │ openpystruct run            │
│ Load Case                │              │   fe.py      OpenSeesPy or  │
│ Optimize / Predict /     │ ◀─────────── │             numpy solver    │
│ Generate Data / Train    │ result.json  │   optimize.py  Adam on I    │
│ Visualize / Deconstruct  │              │   ml/        FNN, PINN      │
└──────────────────────────┘              └─────────────────────────────┘
```

## Install

1. **A container engine.** [Podman Desktop](https://podman-desktop.io/downloads) (free, rootless)
   or Docker Desktop. Start it once.
2. **The engine image.** From a checkout of this repository:
   ```bash
   docker/build.sh            # podman or docker, whichever is installed
   ```
   or in Grasshopper: drop an **Engine** component, wire the checkout path into *Repo*, press
   *Build*. For GPU training build `docker/Dockerfile.cuda` as `openpystruct:cuda`.
3. **The plugin.** Either install the YAK package from Rhino's Package Manager (once published) or
   build from source:
   ```bash
   dotnet build grasshopper/OpenPyStruct.sln
   ```
   The build writes an `OpenPyStruct.ghlink` into your Grasshopper Libraries folder, so the next
   Rhino start loads the dev build. Delete the `.ghlink` to go back to a packaged install.

Apple Silicon: OpenSeesPy publishes Linux wheels for x86_64 only. The native arm64 image falls
back to the package's numpy direct-stiffness solver (same element, same answers to round-off);
set *Platform* to `linux/amd64` on the Engine component to run the emulated OpenSeesPy build.

### Native mode (Apple GPU)

The container is the reproducible default, but it can never reach a Mac's GPU. The Engine
component's *Mode* = `native` runs the same `openpystruct` package with a Python on your machine
instead. Point *Repo* at the checkout and press *Build* once: it creates `~/OpenPyStruct/venv`, installs
torch (the MPS build on macOS) and the package, and *Check* then reports what the engine would pick,
e.g. `"device": "mps"`. Set *Device* to `mps` (or leave `auto`, which prefers CUDA, then MPS, then
CPU). Everything else — case files, run folders, results — is identical in both modes. Wire your own
interpreter into *Python* to skip the venv.

## Workflows

### Optimize a beam

`Beam Model` (a line, 100 elements, roller points) → `Load Case` (points + force vectors, a UDL)
→ `Optimize` (Run) → `Visualize Result`. Add `Material` and `Optimizer Settings` to change the
defaults; they are the beam script's when unwired. Wire several load cases into Optimize to
design for all of them at once.

### Optimize a frame

`Frame Model` (member lines; supports default to fixing the lowest level) → `Load Case` (lateral
forces at nodes, a UDL that lands on the horizontal members) → `Optimize` → `Visualize Result`.
Any planar topology works: the frame script's bays × stories grid is one case of it.

### Train and use a surrogate

`Generate Data` (a few hundred samples first; the paper's sets are 10⁴–10⁵) → `Train Surrogate`
(fnn or pinn) → `Predict` on a `Beam Model` with the same element count and up to *Cases* load
cases. Predict runs the FE analysis on the predicted I so the result is checkable, not a bare
curve. All three take hours at paper scale — the banner shows progress, *Cancel run* is on the
right-click menu, and every run folder keeps `case.json`, `result.json` and its outputs.

## Components

Every component appends the plugin version to its description and has *Open Documentation…* on
its menu, which lands on the matching heading below.

### Beam Model
A straight beam from a curve, split into N equal elements. Pin / Roller / Fixed points snap to
the nearest node. With no points wired the *Scheme* dropdown decides: simply supported (the
scripts' beam), cantilever, propped cantilever, fixed both ends, or continuous on rollers. Draw beams horizontally: gravity is
world −Z, the engine's y is up, and the beam runs along the curve's length. This is the only
model the ML components accept.

### Frame Model
A planar frame from lines or polylines. Endpoints within *Tolerance* merge into nodes, members are
classified column / beam / brace by angle, and the vertical analysis plane is fitted through the
geometry (or given as *Plane*). Out-of-plane geometry is projected with a warning. With no support
points wired, *Base* fixes, pins or puts rollers under every lowest-level node.

### Load Case
Point loads as force vectors (N) at points, snapped to nodes; a uniform load (N/m, negative down)
on the members *UDL on* selects: horizontal members (a whole beam, or a frame's beams), all
members, or the curves wired into *Members*.
The in-plane part of a vector is used; an out-of-plane component raises a warning.

### Material
A *Preset* sets E and ν — the scripts' 200 GPa steel, S355, aluminium 6061, concrete C30/37,
glulam GL24h, or Custom — and wired E / ν override it. A, I₀ (starting I) and k (the shear-area
proxy A = k√I) are plain inputs.

### Optimizer Settings
*Preset* starts from one of the three scripts' parameter sets (Beam, Frame, Training data); any
wired input overrides it. *Combination* says how several load cases combine: Sum adds every case's
energies (the scripts' reading), Envelope designs each element for its worst case. Shared by
Optimize and Generate Data.

### Engine
Image name, CLI path, CPU limit, GPU, platform, FE backend, runs folder, timeout. *Check* probes
the CLI, daemon and image; *Build* builds the image from a repository checkout. Unwired Run
components use the defaults (image `openpystruct`, Podman then Docker, `~/OpenPyStruct/runs`).

### Optimize
Runs the moment-of-inertia optimizer for the wired load cases. Outputs the run as ONE Result item,
the engine log, the run folder, I per element and a summary. The gradient is the scripts' frozen-
force gradient: the section forces are constants within an epoch and only I carries a gradient.

### Predict
Surrogate inference from a trained model bundle (`model.pt`), then an FE check. The beam must
have the model's element count; fewer load cases than the model's cases-per-sample are repeated.

### Generate Data
Random point-load cases on a beam, each optimized, into `dataset.json` in the run folder
(the `StructDataLite.json` layout plus the multicore script's extra fields). *Scale* picks the
sample count when *Samples* is unwired: smoke test (50), development (1 000) or paper (100 000).

### Train Surrogate
*Kind* is FNN (residual MLP predicting I) or PINN (also deflections and rotations, with a physics
loss). Every unwired hyperparameter takes that script's value — fnn 128 × 3, lr 2e-4; pinn 350 × 2,
lr 5e-4 — so switching Kind switches the whole recipe. Writes `model.pt`: architecture, feature
scalers and weights in one file.

### Deconstruct Result
I per element; axial, shear and moment at both element ends; displacement vectors and rotations
per node; the loss history. *Units* gives forces and lengths in N·m, kN·m or kN·mm (I stays in m⁴).
Sign convention: sagging-positive moment, V = dM/dx, tension positive.

### Visualize Result
A box per element with depth (12·I/b)^(1/3) for the chosen width b, coloured by what *Colour by*
picks — I, section depth, or the case's peak bending moment, shear or axial force — over a range
you can pin; moment and shear diagrams as offset polygons per element; the deflected shape.
Scales of 0 fit the diagrams to a tenth of the model.

## Icons

The component icons are a vector set authored as code in [`icons/`](icons/README.md), adapted from
Eddy3D's v4 icon system: one 24-unit canvas, two stroke weights, no effects, one motif plus at most
one badge. Hue means physics rather than ribbon position, so the set uses three families — Structure
amber `#b5821f` for the eight structural components, Prediction purple `#7a5af5` for the three
surrogate components (Eddy3D's ML hue, because a trained network is the same idea in both plugins),
and Tooling grey `#6b7580` for Engine, which is container plumbing.

The motif that carries the plugin's idea is `member`: a beam or column drawn with its section
DEPTH, because depth is what the optimizer designs. Regenerate the PNGs after changing a `def()`:

```bash
cd grasshopper/icons/src && node emit.js
```

A component's glyph is found by its display name alone, so a glyph one character off is silently
unused; `TestIconCoverage` fails the build rather than letting that ship.

## The contract

`case.json` in, `result.json` out, both versioned (`openpystruct.case/1`, `openpystruct.result/1`).
The C# mirror is `OpenPyStruct.Core/Contract`; the Python definition and defaults are
`openpystruct/schema.py`. Units are SI; the 2D model's y axis is up, so gravity loads are
negative. Progress is streamed as `PROGRESS done total value` lines on stdout.

One deliberate deviation from the scripts: they pass `eleLoad -beamUniform udl udl`, which also
applies the UDL as an AXIAL distributed load. The engine applies only the transverse component
of a vertical load (on a frame the original pushes every floor sideways).

## Troubleshooting

- **Run or Build hangs with no output.** Podman's file sharing into its VM can go stale for one
  share: a `-v /Users/...` mount blocks forever while `/private/tmp` works. `podman machine stop`
  then `podman machine start` fixes it. (Base images are `docker.io/`-qualified for the same
  family of problem: Podman's short-name prompt hangs without a terminal.)
- **"No container engine found."** Rhino launched from the Dock does not see your shell PATH;
  the plugin probes `/opt/podman/bin`, Homebrew and Docker Desktop's folders, and honours
  `OPENPYSTRUCT_CONTAINER_CLI` or the Engine component's *CLI* input.
- **"image 'openpystruct' not found."** Build it (step 2 above) — the plugin never pulls.
- **Training runs on the CPU although GPU is on.** A container reaches a GPU only through CUDA on
  an NVIDIA host. On macOS it runs inside a Linux VM with no Metal passthrough, so the switch does
  nothing and `openpystruct info` in the image reports `mps: false`. For an Apple GPU set the
  Engine's *Mode* to `native` and *Device* to `mps` (see below). Train Surrogate reports the device
  it used.
- **Predict: "this model predicts N elements".** Rebuild the beam with N elements; the surrogate
  is tied to the element count it was trained on.

## Development

```bash
python -m pip install -e ".[test]" && python -m pytest        # 22 tests, both FE backends
dotnet test grasshopper/OpenPyStruct.Core.Tests                # 26 tests, Rhino-free
dotnet test grasshopper/OpenPyStruct.Core.Tests --filter FullyQualifiedName~ContainerSmoke   # [Explicit], needs the image
```

Layout:

```
openpystruct/            Python package: fe, optimize, beam, datagen, ml/{features,models,train,predict}, cli
docker/                  Dockerfile (CPU), Dockerfile.cuda, build.sh
grasshopper/
  OpenPyStruct.Core/     Rhino-free: JSON contract, beam/frame builders, container runner, result helpers
  OpenPyStruct.Core.Tests/
  OpenPyStruct.GH/       the .gha: CMP/ components, Types/ wire objects, GUI/ (banner UI copied from Eddy3D)
  manifest.yml           YAK package manifest
```

`GUI/` is copied from Eddy3D so the components get its inline toggles, dropdowns, progress banner
and background-run pattern without an assembly dependency; see `GUI/README.md` for provenance.

## License

The Grasshopper plugin (`OpenPyStruct.GH`, the `.gha`) is **GPL-3.0-or-later** because it links
Eddy3D's GPL component chrome (`OpenPyStruct.GH/GUI/`) — see `LICENSE` in this folder. The Python
engine, the Docker files and `OpenPyStruct.Core` are MIT like the rest of the repository.
