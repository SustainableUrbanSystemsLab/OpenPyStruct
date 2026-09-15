"""``openpystruct run case.json result.json`` — the one entry point the container exposes.

Progress lines are printed to stdout as ``PROGRESS <done> <total> <value>`` so a host process can
drive a progress bar by reading the stream; everything else is free-form logging. The exit code
is 0 when result.json was written with ``ok: true``, 1 otherwise (result.json still written with
the error message, so the host has one place to look).
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
import traceback

from . import __version__
from .fe import LoadCase, Model, opensees_available, resolve_backend
from .schema import CASE_SCHEMA, RESULT_SCHEMA, CaseError, validate_case


def _progress(done: int, total: int, value: float) -> None:
    print(f"PROGRESS {done} {total} {value}", flush=True)


def run_case(case: dict, case_dir: str) -> dict:
    validate_case(case)
    task = case["task"]
    backend = resolve_backend(case.get("fe_backend"))
    print(f"openpystruct {__version__}: task={task} backend={backend}", flush=True)

    material = case["material"]
    optimizer = case["optimizer"]
    params = case["task_params"]
    model = Model.from_json(case["model"]) if case.get("model") else None
    load_cases = [LoadCase.from_json(lc) for lc in case["load_cases"]]

    if task == "optimize":
        from .optimize import optimize
        if model is None:
            raise CaseError("optimize needs a model")
        return optimize(model, load_cases, material, optimizer, backend=backend, progress=_progress)

    if task == "generate_data":
        from .datagen import DEFAULTS, generate
        data = generate(params, material, optimizer, backend=backend, progress=_progress)
        out = params.get("output", DEFAULTS["output"])
        if not os.path.isabs(out):
            out = os.path.join(case_dir, out)
        with open(out, "w", encoding="utf-8") as f:
            json.dump(data, f)
        return {"dataset_path": out, "samples": len(data["I_values"])}

    if task == "train":
        from .ml.train import train
        return train(params, case_dir, progress=_progress, device=case.get("device"))

    if task == "predict":
        from .ml.predict import predict
        if model is None:
            raise CaseError("predict needs a model")
        return predict(params, case_dir, model, load_cases, material, backend=backend,
                       I_min=float(optimizer.get("I_min", 1e-8)))

    raise CaseError(f"unhandled task {task!r}")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(prog="openpystruct")
    sub = parser.add_subparsers(dest="command", required=True)
    run = sub.add_parser("run", help="run a case.json and write result.json")
    run.add_argument("case")
    run.add_argument("result")
    sub.add_parser("info", help="print engine capabilities as JSON")
    args = parser.parse_args(argv)

    if args.command == "info":
        print(json.dumps({
            "version": __version__, "case_schema": CASE_SCHEMA, "result_schema": RESULT_SCHEMA,
            "opensees": opensees_available(),
            "cuda": _cuda_available(),
        }))
        return 0

    t0 = time.time()
    result = {"schema": RESULT_SCHEMA, "task": None, "ok": False, "error": None,
              "engine_version": __version__}
    try:
        with open(args.case, "r", encoding="utf-8") as f:
            case = json.load(f)
        result["task"] = case.get("task")
        payload = run_case(case, os.path.dirname(os.path.abspath(args.case)))
        result.update(payload)
        result["ok"] = True
        code = 0
    except CaseError as exc:
        result["error"] = str(exc)
        code = 1
    except Exception as exc:  # noqa: BLE001 - the message must reach the host
        result["error"] = f"{type(exc).__name__}: {exc}"
        result["traceback"] = traceback.format_exc()
        code = 1
    result["elapsed_s"] = time.time() - t0
    with open(args.result, "w", encoding="utf-8") as f:
        json.dump(result, f)
    if code != 0:
        print(f"ERROR {result['error']}", file=sys.stderr, flush=True)
    else:
        print(f"DONE {result['elapsed_s']:.1f}s", flush=True)
    return code


def _cuda_available() -> bool:
    try:
        import torch
        return bool(torch.cuda.is_available())
    except Exception:
        return False


if __name__ == "__main__":
    sys.exit(main())
