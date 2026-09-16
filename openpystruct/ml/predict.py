"""Surrogate inference — the ``predict`` task.

``task_params``::

    {"model": "model.pt"}          # bundle path (absolute or relative to the case directory)

The case's ``model`` (a beam) and ``load_cases`` are encoded exactly as the training data was.
Fewer load cases than the bundle's ``n_cases`` are repeated cyclically; more is an error. The
predicted I is then run through the FE solver for every load case so the result carries the same
forces and deflections an ``optimize`` run would — a prediction you can check, not just a curve.

Result payload::

    {"I": [...], "cases": [...same as optimize...], "model_kind": "fnn"|"pinn",
     "predicted_deflections": [...] | null, "predicted_rotations": [...] | null}
"""
from __future__ import annotations

import os
from typing import List, Optional

import numpy as np
import torch

from ..beam import describe_beam
from ..fe import LoadCase, Model
from ..optimize import evaluate
from .features import FeatureSpec, Scaler
from .models import FNNWithResidual
from .train import BUNDLE_FORMAT


def load_bundle(path: str) -> dict:
    bundle = torch.load(path, map_location="cpu", weights_only=False)
    if bundle.get("format") != BUNDLE_FORMAT:
        raise ValueError(f"{path} is not an OpenPyStruct model bundle ({bundle.get('format')!r})")
    return bundle


def build_model(bundle: dict) -> FNNWithResidual:
    c = bundle["config"]
    model = FNNWithResidual(c["input_dim"], c["hidden_units"], c["num_blocks"], c["output_dim"], c["dropout"])
    model.load_state_dict(bundle["state_dict"])
    model.eval()
    return model


def predict(params: dict, case_dir: str, model: Model, load_cases: List[LoadCase], material: dict,
            backend: Optional[str] = None, I_min: float = 1e-8) -> dict:
    path = params.get("model", "model.pt")
    if not os.path.isabs(path):
        path = os.path.join(case_dir, path)
    bundle = load_bundle(path)
    spec = FeatureSpec.from_json(bundle["features"])
    y_scaler = Scaler.from_json(bundle["y_scaler"])
    nelem = int(bundle["config"]["nelem"])

    if model.kind != "beam":
        raise ValueError("surrogate prediction is trained on beams; wire a Beam model")
    if model.n_elements != nelem:
        raise ValueError(f"this model predicts {nelem} elements; the beam has {model.n_elements}. "
                         f"Rebuild the beam with {nelem} elements or train a matching model.")
    if not load_cases:
        raise ValueError("predict needs at least one load case")
    if len(load_cases) > spec.n_cases:
        raise ValueError(f"the model was trained on {spec.n_cases} load cases per sample; "
                         f"{len(load_cases)} were given")
    cases = [describe_beam(model, load_cases[i % len(load_cases)]) for i in range(spec.n_cases)]
    x = torch.tensor(spec.encode_cases(cases))
    net = build_model(bundle)
    with torch.no_grad():
        y = net(x).numpy().reshape(1, -1)
    y = y_scaler.inverse(y).reshape(-1)
    I = np.maximum(y[:nelem], I_min)
    defl = y[nelem:2 * nelem + 1].tolist() if bundle["kind"] == "pinn" else None
    rot = y[2 * nelem + 1:].tolist() if bundle["kind"] == "pinn" else None

    return {
        "I": I.tolist(),
        "model_kind": bundle["kind"],
        "model_path": path,
        "predicted_deflections": defl,
        "predicted_rotations": rot,
        "cases": evaluate(model, load_cases, material, I, backend=backend),
    }
