"""Train an FNN or PINN surrogate on a generated dataset — the ``train`` task.

``task_params``::

    {
      "dataset": "dataset.json",     # path (absolute, or relative to the case directory)
      "kind": "fnn" | "pinn",
      "output": "model.pt",          # bundle written next to result.json
      "n_cases": 6, "c": 1.0,
      "hidden_units": 128, "num_blocks": 3, "dropout": 0.5,
      "epochs": 500, "batch_size": 128, "patience": 10,
      "learning_rate": 2e-4, "weight_decay": 1e-2, "gamma": 0.99,
      "sigma_0": 0.03, "gamma_noise": 0.97,
      "initial_alpha": 0.5, "box_constraint_coeff": 0.5,
      "penalty_pinn": 1.5e-6,        # pinn only
      "train_split": 0.8, "seed": 0
    }

The bundle is a single ``torch.save`` dict: architecture config, feature spec, target scaler and
weights — everything :mod:`openpystruct.ml.predict` needs, nothing else.
"""
from __future__ import annotations

import json
import os
import time
from typing import Callable, Optional

import numpy as np
import torch
import torch.nn as nn
from torch.utils.data import DataLoader, TensorDataset

from .features import build_training_arrays
from .models import CompositeLoss, FNNWithResidual, TrainableL1L2Loss

BUNDLE_FORMAT = "openpystruct.model/1"

DEFAULTS = {
    "dataset": "dataset.json",
    "kind": "fnn",
    "output": "model.pt",
    "n_cases": 6,
    "c": 1.0,
    "hidden_units": 128,
    "num_blocks": 3,
    "dropout": 0.5,
    "epochs": 500,
    "batch_size": 128,
    "patience": 10,
    "learning_rate": 2e-4,
    "weight_decay": 1e-2,
    "gamma": 0.99,
    "sigma_0": 0.03,
    "gamma_noise": 0.97,
    "initial_alpha": 0.5,
    "box_constraint_coeff": 0.5,
    "penalty_pinn": 1.5e-6,
    "train_split": 0.8,
    "seed": 0,
}


def resolve_device(requested: Optional[str] = None) -> "torch.device":
    """The device training runs on: an explicit request, else the best available.

    Order is CUDA, then Apple's MPS (Metal), then CPU. MPS matters because a Mac has no CUDA and
    the old check asked for CUDA alone, so training there fell to the CPU silently.

    A GPU is only reachable when the engine runs on the HOST. A container on macOS runs inside a
    Linux VM with no Metal passthrough, so `openpystruct info` inside the image reports mps false
    however the container was started -- the Engine component's GPU switch only means CUDA.
    """
    if requested and requested not in ("auto", ""):
        return torch.device(requested)
    if torch.cuda.is_available():
        return torch.device("cuda")
    mps = getattr(torch.backends, "mps", None)
    if mps is not None and mps.is_available():
        return torch.device("mps")
    return torch.device("cpu")


def train(params: dict, case_dir: str, progress: Optional[Callable[[int, int, float], None]] = None,
          device: Optional[str] = None) -> dict:
    p = dict(DEFAULTS)
    p.update(params or {})
    kind = p["kind"]
    if kind not in ("fnn", "pinn"):
        raise ValueError(f"kind must be 'fnn' or 'pinn', got {kind!r}")
    torch.manual_seed(int(p["seed"]))
    np.random.seed(int(p["seed"]))
    dev = resolve_device(device)
    # Printed, not inferred: training that quietly fell back to the CPU used to look identical to
    # training on a GPU until you timed it.
    print(f"device: {dev.type}", flush=True)

    dataset_path = p["dataset"] if os.path.isabs(p["dataset"]) else os.path.join(case_dir, p["dataset"])
    with open(dataset_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    with_physics = kind == "pinn"
    X, Y, spec, extras = build_training_arrays(data, int(p["n_cases"]), float(p["c"]), with_physics)
    nelem = extras["nelem"]

    n = X.shape[0]
    perm = np.random.permutation(n)
    n_train = max(1, int(float(p["train_split"]) * n))
    tr, va = perm[:n_train], perm[n_train:]
    if len(va) == 0:  # tiny datasets: validate on the training set rather than crash
        va = tr
    Xt, Yt = torch.tensor(X[tr]), torch.tensor(Y[tr])
    Xv, Yv = torch.tensor(X[va]), torch.tensor(Y[va])
    min_c = torch.min(Yt[:, :nelem]).item()
    max_c = torch.max(Yt[:, :nelem]).item()

    model = FNNWithResidual(X.shape[1], int(p["hidden_units"]), int(p["num_blocks"]), Y.shape[1],
                            float(p["dropout"])).to(dev)
    if with_physics:
        criterion = CompositeLoss(nelem, float(p["initial_alpha"]), min_c, max_c,
                                  float(p["box_constraint_coeff"]), float(p["penalty_pinn"])).to(dev)
    else:
        criterion = TrainableL1L2Loss(float(p["initial_alpha"]), min_c, max_c,
                                      float(p["box_constraint_coeff"])).to(dev)

    opt = torch.optim.Adam(list(model.parameters()) + list(criterion.parameters()),
                           lr=float(p["learning_rate"]), weight_decay=float(p["weight_decay"]))
    sched = torch.optim.lr_scheduler.ExponentialLR(opt, gamma=float(p["gamma"]))
    bs = int(p["batch_size"])
    train_loader = DataLoader(TensorDataset(Xt, Yt), batch_size=bs, shuffle=True)
    val_loader = DataLoader(TensorDataset(Xv, Yv), batch_size=bs, shuffle=False)

    epochs = int(p["epochs"])
    patience = int(p["patience"])
    initial_alpha = float(p["initial_alpha"])
    best_val = float("inf")
    best_state = None
    stale = 0
    hist_tr, hist_va = [], []
    t0 = time.time()
    ran = 0
    for epoch in range(1, epochs + 1):
        model.train()
        noise = float(p["sigma_0"]) * (float(p["gamma_noise"]) ** epoch)
        tot = 0.0
        for xb, yb in train_loader:
            xb, yb = xb.to(dev), yb.to(dev)
            xb = xb + torch.randn_like(xb) * noise
            opt.zero_grad()
            preds = model(xb)
            loss = criterion(preds, yb) + (initial_alpha - criterion.alpha) ** 2
            loss.backward()
            nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            opt.step()
            tot += loss.item()
        avg_tr = tot / max(1, len(train_loader))

        model.eval()
        tot = 0.0
        with torch.no_grad():
            for xb, yb in val_loader:
                xb, yb = xb.to(dev), yb.to(dev)
                tot += criterion(model(xb), yb).item()
        avg_va = tot / max(1, len(val_loader))
        sched.step()
        hist_tr.append(avg_tr)
        hist_va.append(avg_va)
        ran = epoch
        if progress:
            progress(epoch, epochs, avg_va)

        if avg_va < best_val:
            best_val = avg_va
            best_state = {k: v.detach().cpu().clone() for k, v in model.state_dict().items()}
            stale = 0
        else:
            stale += 1
            if stale >= patience:
                break

    if best_state is not None:
        model.load_state_dict(best_state)

    bundle = {
        "format": BUNDLE_FORMAT,
        "kind": kind,
        "config": {
            "input_dim": int(X.shape[1]),
            "hidden_units": int(p["hidden_units"]),
            "num_blocks": int(p["num_blocks"]),
            "output_dim": int(Y.shape[1]),
            "dropout": float(p["dropout"]),
            "nelem": int(nelem),
        },
        "features": spec.to_json(),
        "y_scaler": extras["y_scaler"].to_json(),
        "state_dict": {k: v.cpu() for k, v in model.state_dict().items()},
        "training": {
            "epochs": ran, "best_val_loss": best_val, "samples": int(n),
            "train_loss": hist_tr, "val_loss": hist_va, "elapsed_s": time.time() - t0,
        },
    }
    out = p["output"] if os.path.isabs(p["output"]) else os.path.join(case_dir, p["output"])
    torch.save(bundle, out)
    return {
        "model_path": out,
        "kind": kind,
        "epochs": ran,
        "best_val_loss": best_val,
        "samples": int(n),
        "nelem": int(nelem),
        "n_cases": int(p["n_cases"]),
        "device": dev.type,
        "train_loss": hist_tr,
        "val_loss": hist_va,
    }
