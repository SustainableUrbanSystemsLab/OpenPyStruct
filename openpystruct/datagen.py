"""Training-data generation — ``OpenPyStruct_BeamOpt_training_MultiCore.py`` as a task.

Each sample is a beam (fixed geometry and rollers by default, or randomized per sample) under
1..M random point loads plus a UDL, optimized with :func:`openpystruct.optimize.optimize`. The
output is the ``StructDataLite.json`` layout the training scripts read, plus the extra per-sample
fields the multicore script records.

``task_params`` for ``generate_data``::

    {
      "num_samples": 1000,
      "workers": 4,                  # joblib processes; 1 = in-process (deterministic w/ seed)
      "seed": 0,
      "length": 200.0,               # beam length, m (max length when randomized)
      "n_elements": 100,
      "randomize_geometry": false,   # random length in [length_min, length] + random rollers
      "length_min": 15.0,
      "roller_x": [20, 60, 140, 170, 198],   # fixed roller positions (ignored when randomized)
      "max_rollers": 4,
      "max_forces": 4,
      "min_force": -35585.7,
      "max_force": -355857.0,
      "udl": -1000.0,
      "output": "dataset.json"       # written next to result.json; path echoed in the result
    }
"""
from __future__ import annotations

import random
from typing import Callable, Dict, List, Optional

import numpy as np

from .beam import beam_load_case, beam_model, describe_beam
from .optimize import optimize

DATASET_KEYS = [
    "roller_x_locations", "force_x_locations", "force_values", "I_values", "shear_forces",
    "bending_moments", "node_positions", "roller_nodes", "force_nodes", "num_nodes", "L",
    "rotations", "deflections",
]

DEFAULTS = {
    "num_samples": 100,
    "workers": 1,
    "seed": 0,
    "length": 200.0,
    "n_elements": 100,
    "randomize_geometry": False,
    "length_min": 15.0,
    "roller_x": [20.0, 60.0, 140.0, 170.0, 198.0],
    "max_rollers": 4,
    "max_forces": 4,
    "min_force": -35585.7,
    "max_force": -355857.0,
    "udl": -1000.0,
    "output": "dataset.json",
}


def generate_sample(idx: int, params: dict, material: dict, optimizer: dict,
                    backend: Optional[str]) -> Optional[dict]:
    rng = random.Random(int(params["seed"]) * 1_000_003 + idx)
    n_el = int(params["n_elements"])
    if params["randomize_geometry"]:
        L = float(params["length_min"]) + rng.uniform(0.0, float(params["length"]) - float(params["length_min"]))
        x = np.linspace(0.0, L, n_el + 1)
        candidates = list(range(1, n_el + 1))
        n_rollers = rng.randint(1, int(params["max_rollers"]))
        roller_nodes = rng.sample(candidates, n_rollers)
        roller_x = [float(x[i]) for i in roller_nodes]
    else:
        L = float(params["length"])
        x = np.linspace(0.0, L, n_el + 1)
        roller_x = [float(v) for v in params["roller_x"]]
        roller_nodes = [int(np.argmin(np.abs(x - rx))) for rx in roller_x]

    model = beam_model(L, n_el, roller_x)
    available = [i for i in range(1, n_el + 1) if i not in roller_nodes]
    n_forces = rng.randint(1, int(params["max_forces"]))
    force_nodes = rng.sample(available, min(n_forces, len(available)))
    lo, hi = float(params["min_force"]), float(params["max_force"])
    force_values = [rng.uniform(min(lo, hi), max(lo, hi)) for _ in force_nodes]
    lc = beam_load_case(model, [float(x[i]) for i in force_nodes], force_values,
                        udl=float(params["udl"]))
    try:
        res = optimize(model, [lc], material, optimizer, backend=backend)
    except RuntimeError:
        return None
    case = res["cases"][0]
    feat = describe_beam(model, lc)
    disp = np.asarray(case["displacements"])
    return {
        **feat,
        "I_values": res["I"],
        "shear_forces": case["shear_i"],
        "bending_moments": case["moment_i"],
        "roller_nodes": roller_nodes,
        "force_nodes": force_nodes,
        "num_nodes": n_el + 1,
        "L": L,
        "rotations": disp[:, 2].tolist(),
        "deflections": disp[:, 1].tolist(),
    }


def generate(params: dict, material: dict, optimizer: dict, backend: Optional[str] = None,
             progress: Optional[Callable[[int, int, float], None]] = None) -> Dict[str, list]:
    p = dict(DEFAULTS)
    p.update(params or {})
    n = int(p["num_samples"])
    workers = int(p["workers"])
    data: Dict[str, list] = {k: [] for k in DATASET_KEYS}

    def collect(sample: Optional[dict]):
        if sample is None:
            return
        for k in DATASET_KEYS:
            data[k].append(sample[k])

    if workers <= 1:
        for i in range(n):
            collect(generate_sample(i, p, material, optimizer, backend))
            if progress:
                progress(i + 1, n, float("nan"))
    else:
        from joblib import Parallel, delayed  # imported here: optional for single-process use
        batch = max(1, min(500, n // max(workers, 1)))
        for start in range(0, n, batch):
            idx = range(start, min(start + batch, n))
            results = Parallel(n_jobs=workers, backend="loky")(
                delayed(generate_sample)(i, p, material, optimizer, backend) for i in idx)
            for r in results:
                collect(r)
            if progress:
                progress(min(start + batch, n), n, float("nan"))
    return data
