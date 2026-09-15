"""Feature engineering shared by training and inference.

Mirrors sections 2 of ``OpenPyStruct_FNN_MultiCase.py`` / ``_PINN_MultiCase.py``: pad each
ragged list (rollers, force positions, force values, node positions) to a fixed length, group
samples into blocks of ``n_cases`` load cases, standardize each block per feature, concatenate,
and flatten. The scalers are plain mean/scale arrays here (not sklearn objects) so a saved model
bundle is self-describing and inference needs only numpy + torch.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Sequence

import numpy as np

FEATURE_KEYS = ("roller_x", "force_x", "force_values", "node_positions")
DATA_KEYS = {
    "roller_x": "roller_x_locations",
    "force_x": "force_x_locations",
    "force_values": "force_values",
    "node_positions": "node_positions",
}


def pad_sequences(data_list: Sequence[Sequence[float]], max_length: int, pad_val: float = 0.0) -> np.ndarray:
    out = np.full((len(data_list), max_length), pad_val, dtype=np.float32)
    for i, arr in enumerate(data_list):
        a = np.asarray(arr, dtype=np.float32)
        n = min(len(a), max_length)
        out[i, :n] = a[:n]
    return out


@dataclass
class Scaler:
    mean: np.ndarray
    scale: np.ndarray

    @classmethod
    def fit(cls, x2d: np.ndarray) -> "Scaler":
        mean = x2d.mean(axis=0)
        scale = x2d.std(axis=0)
        scale = np.where(scale < 1e-12, 1.0, scale)
        return cls(mean=mean.astype(np.float32), scale=scale.astype(np.float32))

    def transform(self, x: np.ndarray) -> np.ndarray:
        return (x - self.mean) / self.scale

    def inverse(self, x: np.ndarray) -> np.ndarray:
        return x * self.scale + self.mean

    def to_json(self) -> dict:
        return {"mean": self.mean.tolist(), "scale": self.scale.tolist()}

    @classmethod
    def from_json(cls, d: dict) -> "Scaler":
        return cls(mean=np.asarray(d["mean"], dtype=np.float32),
                   scale=np.asarray(d["scale"], dtype=np.float32))


def unify_label_with_c(I_3d: np.ndarray, c: float) -> np.ndarray:
    """(B, n_cases, n_elem) -> (B, n_elem): mean across cases + c * std (conservative envelope)."""
    return I_3d.mean(axis=1) + c * I_3d.std(axis=1)


@dataclass
class FeatureSpec:
    """Everything needed to turn raw beam descriptions into a model input, saved with the model."""
    n_cases: int
    max_lengths: Dict[str, int]
    scalers: Dict[str, Scaler]

    @property
    def per_case_dim(self) -> int:
        return sum(self.max_lengths[k] for k in FEATURE_KEYS)

    @property
    def input_dim(self) -> int:
        return self.n_cases * self.per_case_dim

    def to_json(self) -> dict:
        return {
            "n_cases": self.n_cases,
            "max_lengths": dict(self.max_lengths),
            "scalers": {k: v.to_json() for k, v in self.scalers.items()},
        }

    @classmethod
    def from_json(cls, d: dict) -> "FeatureSpec":
        return cls(n_cases=int(d["n_cases"]), max_lengths={k: int(v) for k, v in d["max_lengths"].items()},
                   scalers={k: Scaler.from_json(v) for k, v in d["scalers"].items()})

    def encode_cases(self, cases: Sequence[dict]) -> np.ndarray:
        """``cases``: n_cases dicts with roller_x_locations, force_x_locations, force_values,
        node_positions. Returns the flat (1, input_dim) float32 input."""
        if len(cases) != self.n_cases:
            raise ValueError(f"expected {self.n_cases} load cases, got {len(cases)}")
        parts = []
        for case in cases:
            sub = []
            for k in FEATURE_KEYS:
                raw = pad_sequences([case[DATA_KEYS[k]]], self.max_lengths[k])
                sub.append(self.scalers[k].transform(raw).reshape(-1))
            parts.append(np.concatenate(sub))
        return np.stack(parts, axis=0).reshape(1, -1).astype(np.float32)


def group_by_cases(arrays: Dict[str, np.ndarray], n_cases: int) -> Dict[str, np.ndarray]:
    """Trim to a multiple of n_cases and reshape (N, M) -> (N // n_cases, n_cases, M)."""
    n = next(iter(arrays.values())).shape[0]
    total = n // n_cases
    if total == 0:
        raise ValueError(f"n_cases={n_cases} exceeds the {n} samples in the dataset")
    trim = total * n_cases
    return {k: v[:trim].reshape(total, n_cases, -1) for k, v in arrays.items()}


def build_training_arrays(data: dict, n_cases: int, c: float, with_physics: bool):
    """From a dataset dict -> (X_flat, Y, spec, extras).

    ``Y`` is (B, n_elem) for FNN, or (B, n_elem + 2 (n_elem + 1)) = [I | deflections |
    rotations] for the PINN. ``extras`` carries the target scaler and layout for the bundle.
    """
    raw = {k: data[DATA_KEYS[k]] for k in FEATURE_KEYS}
    I_values = data["I_values"]
    n = len(I_values)
    for k, v in raw.items():
        if len(v) != n:
            raise ValueError(f"dataset field {DATA_KEYS[k]} has {len(v)} rows, I_values has {n}")
    max_lengths = {k: max((len(r) for r in v), default=0) for k, v in raw.items()}
    nelem = max(len(r) for r in I_values)

    padded = {k: pad_sequences(v, max_lengths[k]) for k, v in raw.items()}
    padded["I"] = pad_sequences(I_values, nelem)
    if with_physics:
        padded["defl"] = pad_sequences(data["deflections"], nelem + 1)
        padded["rot"] = pad_sequences(data["rotations"], nelem + 1)
    grouped = group_by_cases(padded, n_cases)

    scalers = {}
    feats = []
    for k in FEATURE_KEYS:
        g = grouped[k]
        B, NC, M = g.shape
        sc = Scaler.fit(g.reshape(B * NC, M))
        scalers[k] = sc
        feats.append(sc.transform(g.reshape(B * NC, M)).reshape(B, NC, M))
    X = np.concatenate(feats, axis=2).reshape(grouped["I"].shape[0], -1).astype(np.float32)

    Y_I = unify_label_with_c(grouped["I"], c)
    if with_physics:
        Y = np.concatenate([Y_I, grouped["defl"].mean(axis=1), grouped["rot"].mean(axis=1)], axis=1)
    else:
        Y = Y_I
    y_scaler = Scaler.fit(Y)
    Y_std = y_scaler.transform(Y).astype(np.float32)

    spec = FeatureSpec(n_cases=n_cases, max_lengths=max_lengths, scalers=scalers)
    extras = {"y_scaler": y_scaler, "nelem": nelem, "with_physics": with_physics}
    return X, Y_std, spec, extras
