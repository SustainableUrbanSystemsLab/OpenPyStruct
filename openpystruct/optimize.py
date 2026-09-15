"""Gradient-based moment-of-inertia optimization — the classical OpenPyStruct optimizer.

This is ``OpenPyStruct_BeamOpt.py`` and ``OpenPyStruct_FrameOpt_Discrete_Beta.py`` factored into
one routine over the generic 2D frame of :mod:`openpystruct.fe`, and extended to several load
cases (energies are summed across cases; sum(I) is counted once).

The loss, per the scripts::

    total = sum(I) + alpha_moment * sum(M_i^2 / (2 E I)) + alpha_shear * sum(V_i^2 / (G k sqrt(I)))

Gradient flow is also faithful to the scripts: the section forces are treated as constants at
each epoch (they come out of the FE solve as plain numbers) and only the explicit ``I`` in the
denominators carries a gradient. Adam + exponential LR decay, clamp ``I >= I_min`` after every
step, early stop on ``patience`` epochs without a ``tolerance`` improvement.

Progress is reported through ``progress(epoch, total_epochs, loss)`` so the caller (the CLI, and
through it the Grasshopper component) can draw a bar without knowing anything else.
"""
from __future__ import annotations

from typing import Callable, List, Optional, Sequence

import numpy as np
import torch

from .fe import Analysis, LoadCase, Model, analyze

Progress = Callable[[int, int, float], None]


def shear_modulus(E: float, nu: float) -> float:
    return E / (2.0 * (1.0 + nu))


def loss_terms(I: torch.Tensor, analyses: Sequence[Analysis], E: float, G: float, k: float,
               alpha_moment: float, alpha_shear: float):
    """(total, primary, bending, shear) for the current I and the frozen section forces."""
    primary = torch.sum(I)
    bending = torch.zeros((), dtype=I.dtype)
    shear = torch.zeros((), dtype=I.dtype)
    A_local = k * torch.sqrt(I)
    for a in analyses:
        M = torch.as_tensor(a.M_i, dtype=I.dtype)
        V = torch.as_tensor(a.V_i, dtype=I.dtype)
        bending = bending + torch.sum(M * M / (2.0 * E * I + 1e-6))
        shear = shear + torch.sum(V * V / (G * A_local))
    return (primary + alpha_moment * bending + alpha_shear * shear,
            primary, alpha_moment * bending, alpha_shear * shear)


def optimize(model: Model, load_cases: List[LoadCase], material: dict, optimizer: dict,
             backend: Optional[str] = None, progress: Optional[Progress] = None,
             I_init: Optional[Sequence[float]] = None) -> dict:
    """Run the optimization. Returns the result payload (JSON-ready) for the ``optimize`` task.

    Payload::

        {
          "I": [...],                       # optimized moment of inertia per element
          "epochs": n,                      # epochs actually run
          "stopped_early": bool,
          "loss_history": {"total": [...], "primary": [...], "bending": [...], "shear": [...]},
          "cases": [ {"name", "displacements", "axial", "shear_i", "shear_j",
                      "moment_i", "moment_j"}, ... ]   # final analysis per load case
        }
    """
    if not load_cases:
        raise ValueError("optimize needs at least one load case")

    E = float(material["E"])
    G = shear_modulus(E, float(material["nu"]))
    A = float(material["A"])
    k = float(material["k"])
    I0 = float(material["I0"])

    epochs = int(optimizer["epochs"])
    lr = float(optimizer["lr"])
    gamma = float(optimizer["gamma"])
    alpha_m = float(optimizer["alpha_moment"])
    alpha_s = float(optimizer["alpha_shear"])
    tol = float(optimizer["tolerance"])
    patience = int(optimizer["patience"])
    I_min = float(optimizer.get("I_min", 1e-8))

    m = model.n_elements
    init = np.full(m, I0) if I_init is None else np.asarray(I_init, dtype=float)
    I = torch.tensor(init, dtype=torch.float64, requires_grad=True)
    opt = torch.optim.Adam([I], lr=lr)
    sched = torch.optim.lr_scheduler.ExponentialLR(opt, gamma=gamma)

    history = {"total": [], "primary": [], "bending": [], "shear": []}
    best = float("inf")
    stale = 0
    stopped_early = False
    ran = 0
    analyses: List[Analysis] = []

    for epoch in range(epochs):
        opt.zero_grad()
        I_np = I.detach().cpu().numpy()
        analyses = [analyze(model, lc, E, A, I_np, backend=backend) for lc in load_cases]
        total, primary, bending, shear = loss_terms(I, analyses, E, G, k, alpha_m, alpha_s)
        total.backward()
        opt.step()
        sched.step()
        with torch.no_grad():
            I.clamp_(min=I_min)

        t = float(total.item())
        history["total"].append(t)
        history["primary"].append(float(primary.item()))
        history["bending"].append(float(bending.item()))
        history["shear"].append(float(shear.item()))
        ran = epoch + 1
        if progress is not None:
            progress(ran, epochs, t)

        if t < best - tol:
            best = t
            stale = 0
        else:
            stale += 1
        if stale >= patience:
            stopped_early = True
            break

    I_final = I.detach().cpu().numpy()
    final = [analyze(model, lc, E, A, I_final, backend=backend) for lc in load_cases]
    return {
        "I": I_final.tolist(),
        "epochs": ran,
        "stopped_early": stopped_early,
        "best_loss": best if best != float("inf") else None,
        "loss_history": history,
        "cases": [dict(name=lc.name, **a.to_json()) for lc, a in zip(load_cases, final)],
    }


def evaluate(model: Model, load_cases: List[LoadCase], material: dict, I: Sequence[float],
             backend: Optional[str] = None) -> List[dict]:
    """Plain analysis of every load case for a GIVEN I (used after a surrogate prediction)."""
    E, A = float(material["E"]), float(material["A"])
    return [dict(name=lc.name, **analyze(model, lc, E, A, I, backend=backend).to_json())
            for lc in load_cases]
