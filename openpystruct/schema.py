"""The JSON contract between the Grasshopper plugin (C#) and this package.

Everything the plugin sends is one ``case.json`` document; everything it reads back is one
``result.json``. Both carry a ``schema`` string so either side can refuse a version it does not
understand instead of misreading fields.

Units are SI throughout: metres, newtons, pascals, N/m for distributed loads, m^4 for I.
Sign convention follows the original OpenPyStruct scripts: the 2D model's y axis points UP, so a
gravity load is NEGATIVE.

case.json
---------
::

    {
      "schema": "openpystruct.case/1",
      "task": "optimize" | "generate_data" | "train" | "predict",
      "model": {                      # a 2D frame; a beam is a frame with collinear nodes
        "nodes": [[x, y], ...],
        "elements": [[i, j], ...],    # 0-based node indices
        "supports": [{"node": i, "type": "pin" | "roller" | "fixed"}, ...],
        "kind": "beam" | "frame"
      },
      "material": {"E": Pa, "nu": -, "A": m2, "I0": m4, "k": -},
      "load_cases": [
        {
          "name": "LC1",
          "point_loads": [{"node": i, "fx": N, "fy": N, "mz": Nm}, ...],
          "element_loads": [{"element": e, "wy": N/m}, ...]
        }, ...
      ],
      "optimizer": {"epochs", "lr", "gamma", "alpha_moment", "alpha_shear", "tolerance", "patience"},
      "task_params": { ... task specific, see the task modules ... }
    }

result.json
-----------
::

    {
      "schema": "openpystruct.result/1",
      "task": "...",
      "ok": true | false,
      "error": null | "message",
      "elapsed_s": float,
      ... task specific payload (see each task module's docstring) ...
    }
"""

CASE_SCHEMA = "openpystruct.case/1"
RESULT_SCHEMA = "openpystruct.result/1"

TASKS = ("optimize", "generate_data", "train", "predict")

SUPPORT_TYPES = ("pin", "roller", "fixed")

DEFAULT_MATERIAL = {
    "E": 200e9,   # Young's modulus, Pa (steel)
    "nu": 0.3,    # Poisson's ratio
    "A": 0.01,    # cross-sectional area used by the FE model, m^2
    "I0": 0.5,    # initial moment of inertia guess, m^4
    "k": 0.03,    # A_local = k * sqrt(I): the section-area proxy in the shear-energy term
}

DEFAULT_OPTIMIZER = {
    "epochs": 1000,
    "lr": 0.01,
    "gamma": 0.98,
    "alpha_moment": 1e-2,
    "alpha_shear": 1e-2,
    "tolerance": 1e-2,
    "patience": 10,
    "I_min": 1e-8,
    # how several load cases combine: "sum" adds every case's energies (the scripts' multi-case
    # reading); "envelope" takes, per element, the worst case's bending and shear energy, so the
    # design is governed by whichever case hurts each element most.
    "combination": "sum",
}


class CaseError(ValueError):
    """A case.json that cannot be run. The message is shown verbatim in Grasshopper."""


def validate_case(case: dict) -> dict:
    """Check the parts every task shares and fill in defaults. Returns the same dict."""
    if not isinstance(case, dict):
        raise CaseError("case.json must be a JSON object")
    schema = case.get("schema")
    if schema != CASE_SCHEMA:
        raise CaseError(f"unsupported case schema {schema!r}; this engine speaks {CASE_SCHEMA}")
    task = case.get("task")
    if task not in TASKS:
        raise CaseError(f"unknown task {task!r}; expected one of {TASKS}")

    material = dict(DEFAULT_MATERIAL)
    material.update(case.get("material") or {})
    case["material"] = material

    optimizer = dict(DEFAULT_OPTIMIZER)
    optimizer.update(case.get("optimizer") or {})
    case["optimizer"] = optimizer

    case.setdefault("task_params", {})
    case.setdefault("load_cases", [])

    if "model" in case and case["model"] is not None:
        validate_model(case["model"])
        for lc in case["load_cases"]:
            validate_load_case(lc, case["model"])
    return case


def validate_model(model: dict) -> None:
    nodes = model.get("nodes")
    elements = model.get("elements")
    if not nodes or len(nodes) < 2:
        raise CaseError("model needs at least two nodes")
    if not elements:
        raise CaseError("model needs at least one element")
    n = len(nodes)
    for idx, xy in enumerate(nodes):
        if len(xy) != 2:
            raise CaseError(f"node {idx} must be [x, y]")
    for idx, ij in enumerate(elements):
        if len(ij) != 2 or not all(0 <= k < n for k in ij):
            raise CaseError(f"element {idx} references a node outside 0..{n - 1}")
        if ij[0] == ij[1]:
            raise CaseError(f"element {idx} connects node {ij[0]} to itself")
    supports = model.get("supports") or []
    if not supports:
        raise CaseError("model has no supports; the structure is a mechanism")
    for s in supports:
        if s.get("type") not in SUPPORT_TYPES:
            raise CaseError(f"support type {s.get('type')!r} not in {SUPPORT_TYPES}")
        if not (0 <= s.get("node", -1) < n):
            raise CaseError(f"support references node {s.get('node')} outside 0..{n - 1}")
    model.setdefault("kind", "frame")


def validate_load_case(lc: dict, model: dict) -> None:
    n = len(model["nodes"])
    m = len(model["elements"])
    lc.setdefault("name", "LC")
    lc.setdefault("point_loads", [])
    lc.setdefault("element_loads", [])
    for p in lc["point_loads"]:
        if not (0 <= p.get("node", -1) < n):
            raise CaseError(f"point load references node {p.get('node')} outside 0..{n - 1}")
        p.setdefault("fx", 0.0)
        p.setdefault("fy", 0.0)
        p.setdefault("mz", 0.0)
    for e in lc["element_loads"]:
        if not (0 <= e.get("element", -1) < m):
            raise CaseError(f"element load references element {e.get('element')} outside 0..{m - 1}")
        e.setdefault("wy", 0.0)
