"""Beam helpers: build the straight, evenly discretized beam model the ML pipeline is trained on.

The surrogates (:mod:`openpystruct.ml`) learn on beams described by roller x-positions, point
load x-positions and values, and node positions — the ``StructDataLite.json`` layout of the
original scripts. This module turns those descriptions into :class:`~openpystruct.fe.Model` /
:class:`~openpystruct.fe.LoadCase` pairs and back.
"""
from __future__ import annotations

from typing import List, Sequence, Tuple

import numpy as np

from .fe import LoadCase, Model


def beam_model(length: float, n_elements: int, roller_x: Sequence[float],
               pin_x: Sequence[float] = (0.0,), fixed_x: Sequence[float] = ()) -> Model:
    """A straight beam along +x from 0 to ``length`` with ``n_elements`` equal elements.

    Supports snap to the nearest node. Default: a pin at x=0 (the scripts' ``fix(1,1,1,0)``).
    """
    x = np.linspace(0.0, float(length), int(n_elements) + 1)
    nodes = np.column_stack([x, np.zeros_like(x)])
    elements = np.column_stack([np.arange(n_elements), np.arange(1, n_elements + 1)])
    supports = []
    for px in pin_x:
        supports.append({"node": nearest_node(x, px), "type": "pin"})
    for rx in roller_x:
        supports.append({"node": nearest_node(x, rx), "type": "roller"})
    for fx in fixed_x:
        supports.append({"node": nearest_node(x, fx), "type": "fixed"})
    return Model(nodes=nodes, elements=elements, supports=supports, kind="beam")


def beam_load_case(model: Model, force_x: Sequence[float], force_values: Sequence[float],
                   udl: float = 0.0, name: str = "LC") -> LoadCase:
    """Point loads (global y, negative = down) snapped to nodes plus one UDL on every element."""
    x = model.nodes[:, 0]
    point = [{"node": nearest_node(x, px), "fx": 0.0, "fy": float(v), "mz": 0.0}
             for px, v in zip(force_x, force_values)]
    elem = [{"element": e, "wy": float(udl)} for e in range(model.n_elements)] if udl else []
    return LoadCase(name=name, point_loads=point, element_loads=elem)


def nearest_node(x: np.ndarray, px: float) -> int:
    return int(np.argmin(np.abs(np.asarray(x) - float(px))))


def describe_beam(model: Model, lc: LoadCase) -> dict:
    """The ML feature description (roller_x, force_x, force_values, node_positions) of a beam
    model + load case. Inverse of :func:`beam_model` / :func:`beam_load_case`."""
    x = model.nodes[:, 0]
    roller_x = sorted(float(x[s["node"]]) for s in model.supports if s["type"] == "roller")
    pairs: List[Tuple[float, float]] = sorted(
        (float(x[p["node"]]), float(p["fy"])) for p in lc.point_loads if p.get("fy", 0.0) != 0.0)
    return {
        "roller_x_locations": roller_x,
        "force_x_locations": [p[0] for p in pairs],
        "force_values": [p[1] for p in pairs],
        "node_positions": x.tolist(),
    }
