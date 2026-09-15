"""Linear-elastic 2D frame analysis.

Two interchangeable backends:

* ``opensees`` — OpenSeesPy ``elasticBeamColumn`` with a ``Linear`` geometric transform, exactly
  what the original OpenPyStruct scripts use. Preferred when importable.
* ``numpy`` — a direct-stiffness Euler–Bernoulli frame solver. Same element, same answers to
  round-off; exists so the package tests on any machine and so a container without OpenSees
  (e.g. an arm64 image, where OpenSeesPy has no wheel) still works.

Both return an :class:`Analysis` in ONE convention so nothing downstream cares which ran:

* ``disp[n] = (ux, uy, rz)`` global nodal displacements;
* per element ``N`` (axial, tension positive), ``V_i, V_j`` (shear) and ``M_i, M_j`` (bending) as
  INTERNAL section forces at the two ends, sagging positive for ``M`` and ``V = dM/dx``.
  A simply supported beam under a downward UDL therefore has a POSITIVE mid-span moment.

Element loads are given as a GLOBAL-y load per unit length ``wy`` (negative = gravity) and are
resolved into the member's local axes here, so the plugin never needs to know a member's tilt.

One deliberate deviation from the scripts: they pass ``eleLoad -beamUniform udl udl`` which sets
the AXIAL distributed load to the same value as the transverse one. On a beam that only adds axial
force; on a frame it pushes every floor sideways. Here only the transverse component of a vertical
load is applied — the physics the scripts describe, not the typo.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional, Sequence

import numpy as np

try:  # OpenSeesPy is optional at import time so the numpy backend works without it.
    import openseespy.opensees as _ops  # type: ignore
except Exception:  # pragma: no cover - depends on the environment
    _ops = None


def opensees_available() -> bool:
    return _ops is not None


@dataclass
class Model:
    nodes: np.ndarray            # (n, 2)
    elements: np.ndarray         # (m, 2) int
    supports: List[dict]         # {"node": i, "type": "pin"|"roller"|"fixed"}
    kind: str = "frame"

    @classmethod
    def from_json(cls, model: dict) -> "Model":
        return cls(
            nodes=np.asarray(model["nodes"], dtype=float),
            elements=np.asarray(model["elements"], dtype=int),
            supports=list(model.get("supports") or []),
            kind=model.get("kind", "frame"),
        )

    @property
    def n_nodes(self) -> int:
        return int(self.nodes.shape[0])

    @property
    def n_elements(self) -> int:
        return int(self.elements.shape[0])

    def lengths(self) -> np.ndarray:
        d = self.nodes[self.elements[:, 1]] - self.nodes[self.elements[:, 0]]
        return np.hypot(d[:, 0], d[:, 1])

    def directions(self) -> np.ndarray:
        """Unit (cos, sin) per element."""
        d = self.nodes[self.elements[:, 1]] - self.nodes[self.elements[:, 0]]
        L = np.hypot(d[:, 0], d[:, 1])
        return d / L[:, None]

    def fixity(self) -> np.ndarray:
        """(n, 3) bool: which of ux, uy, rz is restrained at each node."""
        fix = np.zeros((self.n_nodes, 3), dtype=bool)
        for s in self.supports:
            i = int(s["node"])
            t = s["type"]
            if t == "pin":
                fix[i, 0] = fix[i, 1] = True
            elif t == "roller":
                fix[i, 1] = True
            elif t == "fixed":
                fix[i, :] = True
            else:
                raise ValueError(f"unknown support type {t!r}")
        return fix


@dataclass
class LoadCase:
    name: str = "LC"
    point_loads: List[dict] = field(default_factory=list)     # {"node", "fx", "fy", "mz"}
    element_loads: List[dict] = field(default_factory=list)   # {"element", "wy"}

    @classmethod
    def from_json(cls, lc: dict) -> "LoadCase":
        return cls(
            name=lc.get("name", "LC"),
            point_loads=list(lc.get("point_loads") or []),
            element_loads=list(lc.get("element_loads") or []),
        )

    def element_wy(self, n_elements: int) -> np.ndarray:
        wy = np.zeros(n_elements)
        for e in self.element_loads:
            wy[int(e["element"])] += float(e.get("wy", 0.0))
        return wy

    def nodal(self, n_nodes: int) -> np.ndarray:
        f = np.zeros((n_nodes, 3))
        for p in self.point_loads:
            i = int(p["node"])
            f[i, 0] += float(p.get("fx", 0.0))
            f[i, 1] += float(p.get("fy", 0.0))
            f[i, 2] += float(p.get("mz", 0.0))
        return f


@dataclass
class Analysis:
    disp: np.ndarray    # (n, 3)
    N: np.ndarray       # (m,)
    V_i: np.ndarray
    V_j: np.ndarray
    M_i: np.ndarray
    M_j: np.ndarray

    def to_json(self) -> dict:
        return {
            "displacements": self.disp.tolist(),
            "axial": self.N.tolist(),
            "shear_i": self.V_i.tolist(),
            "shear_j": self.V_j.tolist(),
            "moment_i": self.M_i.tolist(),
            "moment_j": self.M_j.tolist(),
        }


def analyze(model: Model, load: LoadCase, E: float, A: float, I: Sequence[float],
            backend: Optional[str] = None) -> Analysis:
    """Solve one load case. ``backend`` is ``"opensees"``, ``"numpy"`` or None for auto."""
    backend = resolve_backend(backend)
    I = np.asarray(I, dtype=float)
    if I.shape != (model.n_elements,):
        raise ValueError(f"I must have one value per element ({model.n_elements}), got {I.shape}")
    if backend == "opensees":
        return _analyze_opensees(model, load, E, A, I)
    return _analyze_numpy(model, load, E, A, I)


def resolve_backend(backend: Optional[str]) -> str:
    if backend in (None, "", "auto"):
        return "opensees" if opensees_available() else "numpy"
    if backend == "opensees" and not opensees_available():
        raise RuntimeError("backend 'opensees' requested but openseespy is not importable")
    if backend not in ("opensees", "numpy"):
        raise ValueError(f"unknown FE backend {backend!r}")
    return backend


# --------------------------------------------------------------------------------------------
# Shared element math
# --------------------------------------------------------------------------------------------

def _local_stiffness(E: float, A: float, I: float, L: float) -> np.ndarray:
    EA_L = E * A / L
    EI = E * I
    L2, L3 = L * L, L * L * L
    k = np.array([
        [EA_L, 0, 0, -EA_L, 0, 0],
        [0, 12 * EI / L3, 6 * EI / L2, 0, -12 * EI / L3, 6 * EI / L2],
        [0, 6 * EI / L2, 4 * EI / L, 0, -6 * EI / L2, 2 * EI / L],
        [-EA_L, 0, 0, EA_L, 0, 0],
        [0, -12 * EI / L3, -6 * EI / L2, 0, 12 * EI / L3, -6 * EI / L2],
        [0, 6 * EI / L2, 2 * EI / L, 0, -6 * EI / L2, 4 * EI / L],
    ])
    return k


def _transform(c: float, s: float) -> np.ndarray:
    """Global -> local rotation for [u, v, r] x 2."""
    T = np.zeros((6, 6))
    r = np.array([[c, s, 0], [-s, c, 0], [0, 0, 1]])
    T[:3, :3] = r
    T[3:, 3:] = r
    return T


def _uniform_local_components(wy_global: float, c: float, s: float):
    """A global-y load per unit length resolved into local (axial wx, transverse wy)."""
    return wy_global * s, wy_global * c


def _fixed_end_equivalent_local(wx: float, wy: float, L: float) -> np.ndarray:
    """Equivalent NODAL loads (local) of a uniform member load: what to ADD to the load vector."""
    return np.array([wx * L / 2, wy * L / 2, wy * L * L / 12,
                     wx * L / 2, wy * L / 2, -wy * L * L / 12])


def _internal_from_end_forces(f: np.ndarray):
    """End forces acting ON the element (local) -> internal N, V_i, V_j, M_i, M_j.

    Sagging positive moment, V = dM/dx, tension positive axial. At end i the section force is
    the negative of the force the node applies to the element; at end j it is the same force.
    """
    N = f[3]
    V_i, V_j = f[1], -f[4]
    M_i, M_j = -f[2], f[5]
    return N, V_i, V_j, M_i, M_j


# --------------------------------------------------------------------------------------------
# numpy backend
# --------------------------------------------------------------------------------------------

def _analyze_numpy(model: Model, load: LoadCase, E: float, A: float, I: np.ndarray) -> Analysis:
    n, m = model.n_nodes, model.n_elements
    ndof = 3 * n
    K = np.zeros((ndof, ndof))
    F = load.nodal(n).reshape(-1).astype(float)
    L = model.lengths()
    dirs = model.directions()
    wy = load.element_wy(m)

    per_elem = []
    for e in range(m):
        i, j = model.elements[e]
        c, s = dirs[e]
        T = _transform(c, s)
        k_loc = _local_stiffness(E, A, I[e], L[e])
        k_glob = T.T @ k_loc @ T
        dofs = np.array([3 * i, 3 * i + 1, 3 * i + 2, 3 * j, 3 * j + 1, 3 * j + 2])
        K[np.ix_(dofs, dofs)] += k_glob
        wx_l, wy_l = _uniform_local_components(wy[e], c, s)
        f_eq_loc = _fixed_end_equivalent_local(wx_l, wy_l, L[e])
        F[dofs] += T.T @ f_eq_loc
        per_elem.append((dofs, T, k_loc, f_eq_loc))

    fixed = model.fixity().reshape(-1)
    free = ~fixed
    if not free.any():
        u = np.zeros(ndof)
    else:
        Kff = K[np.ix_(free, free)]
        try:
            u_f = np.linalg.solve(Kff, F[free])
        except np.linalg.LinAlgError as exc:
            raise RuntimeError("singular stiffness matrix: the structure is a mechanism "
                               "(check supports and connectivity)") from exc
        u = np.zeros(ndof)
        u[free] = u_f

    N = np.zeros(m); V_i = np.zeros(m); V_j = np.zeros(m); M_i = np.zeros(m); M_j = np.zeros(m)
    for e, (dofs, T, k_loc, f_eq_loc) in enumerate(per_elem):
        u_loc = T @ u[dofs]
        f_end = k_loc @ u_loc - f_eq_loc   # forces the nodes exert on the element
        N[e], V_i[e], V_j[e], M_i[e], M_j[e] = _internal_from_end_forces(f_end)

    return Analysis(disp=u.reshape(n, 3), N=N, V_i=V_i, V_j=V_j, M_i=M_i, M_j=M_j)


# --------------------------------------------------------------------------------------------
# OpenSeesPy backend
# --------------------------------------------------------------------------------------------

def _analyze_opensees(model: Model, load: LoadCase, E: float, A: float, I: np.ndarray) -> Analysis:
    ops = _ops
    n, m = model.n_nodes, model.n_elements
    ops.wipe()
    ops.model("basic", "-ndm", 2, "-ndf", 3)
    for i in range(n):
        ops.node(i + 1, float(model.nodes[i, 0]), float(model.nodes[i, 1]))
    fix = model.fixity()
    for i in range(n):
        if fix[i].any():
            ops.fix(i + 1, int(fix[i, 0]), int(fix[i, 1]), int(fix[i, 2]))
    ops.geomTransf("Linear", 1)
    for e in range(m):
        i, j = model.elements[e]
        ops.element("elasticBeamColumn", e + 1, int(i) + 1, int(j) + 1, float(A), float(E),
                    float(I[e]), 1)
    ops.timeSeries("Linear", 1)
    ops.pattern("Plain", 1, 1)
    nodal = load.nodal(n)
    for i in range(n):
        if nodal[i].any():
            ops.load(i + 1, float(nodal[i, 0]), float(nodal[i, 1]), float(nodal[i, 2]))
    wy = load.element_wy(m)
    dirs = model.directions()
    for e in range(m):
        if wy[e] != 0.0:
            c, s = dirs[e]
            wx_l, wy_l = _uniform_local_components(wy[e], c, s)
            # -beamUniform Wy Wx : transverse first, axial second (2D)
            ops.eleLoad("-ele", e + 1, "-type", "-beamUniform", float(wy_l), float(wx_l))
    ops.system("BandGeneral")
    ops.numberer("RCM")
    ops.constraints("Plain")
    ops.integrator("LoadControl", 1.0)
    ops.algorithm("Linear")
    ops.analysis("Static")
    ok = ops.analyze(1)
    if ok != 0:
        raise RuntimeError(f"OpenSees static analysis failed (code {ok})")

    disp = np.array([[ops.nodeDisp(i + 1, 1), ops.nodeDisp(i + 1, 2), ops.nodeDisp(i + 1, 3)]
                     for i in range(n)])
    N = np.zeros(m); V_i = np.zeros(m); V_j = np.zeros(m); M_i = np.zeros(m); M_j = np.zeros(m)
    for e in range(m):
        f = np.asarray(ops.eleResponse(e + 1, "localForce"), dtype=float)
        N[e], V_i[e], V_j[e], M_i[e], M_j[e] = _internal_from_end_forces(f)
    return Analysis(disp=disp, N=N, V_i=V_i, V_j=V_j, M_i=M_i, M_j=M_j)
