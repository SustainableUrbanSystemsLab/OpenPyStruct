import json

import numpy as np

from openpystruct.cli import main
from openpystruct.schema import CASE_SCHEMA, RESULT_SCHEMA


def _beam_case(task="optimize", **extra):
    x = np.linspace(0, 20, 11)
    return {
        "schema": CASE_SCHEMA,
        "task": task,
        "fe_backend": "numpy",
        "model": {
            "kind": "beam",
            "nodes": [[float(v), 0.0] for v in x],
            "elements": [[i, i + 1] for i in range(10)],
            "supports": [{"node": 0, "type": "pin"}, {"node": 5, "type": "roller"}, {"node": 10, "type": "roller"}],
        },
        "load_cases": [{"name": "LC1", "point_loads": [{"node": 3, "fy": -1e5}],
                        "element_loads": [{"element": e, "wy": -1e3} for e in range(10)]}],
        "optimizer": {"epochs": 10, "patience": 100},
        **extra,
    }


def test_cli_optimize_roundtrip(tmp_path, capsys):
    case = tmp_path / "case.json"
    result = tmp_path / "result.json"
    case.write_text(json.dumps(_beam_case()))
    assert main(["run", str(case), str(result)]) == 0
    out = json.loads(result.read_text())
    assert out["schema"] == RESULT_SCHEMA and out["ok"] and out["error"] is None
    assert len(out["I"]) == 10 and out["epochs"] == 10
    stdout = capsys.readouterr().out
    assert "PROGRESS 10 10" in stdout and "DONE" in stdout


def test_cli_reports_case_errors(tmp_path):
    case = tmp_path / "case.json"
    result = tmp_path / "result.json"
    bad = _beam_case()
    bad["model"]["supports"] = []
    case.write_text(json.dumps(bad))
    assert main(["run", str(case), str(result)]) == 1
    out = json.loads(result.read_text())
    assert not out["ok"] and "supports" in out["error"]


def test_cli_rejects_wrong_schema(tmp_path):
    case = tmp_path / "case.json"
    result = tmp_path / "result.json"
    c = _beam_case()
    c["schema"] = "openpystruct.case/99"
    case.write_text(json.dumps(c))
    assert main(["run", str(case), str(result)]) == 1
    assert "schema" in json.loads(result.read_text())["error"]


def test_cli_generate_train_predict_pipeline(tmp_path):
    gen = tmp_path / "gen.json"
    gen.write_text(json.dumps({
        "schema": CASE_SCHEMA, "task": "generate_data", "fe_backend": "numpy",
        "optimizer": {"epochs": 3, "patience": 100},
        "task_params": {"num_samples": 6, "n_elements": 4, "length": 8.0, "roller_x": [8.0],
                        "max_forces": 1, "output": "dataset.json"},
    }))
    assert main(["run", str(gen), str(tmp_path / "r1.json")]) == 0
    assert (tmp_path / "dataset.json").exists()

    tr = tmp_path / "train.json"
    tr.write_text(json.dumps({
        "schema": CASE_SCHEMA, "task": "train",
        "task_params": {"dataset": "dataset.json", "kind": "fnn", "n_cases": 2, "epochs": 2,
                        "hidden_units": 8, "num_blocks": 1, "batch_size": 4, "output": "model.pt"},
    }))
    assert main(["run", str(tr), str(tmp_path / "r2.json")]) == 0
    r2 = json.loads((tmp_path / "r2.json").read_text())
    assert r2["ok"] and r2["nelem"] == 4

    x = np.linspace(0, 8, 5)
    pr = tmp_path / "predict.json"
    pr.write_text(json.dumps({
        "schema": CASE_SCHEMA, "task": "predict", "fe_backend": "numpy",
        "model": {"kind": "beam", "nodes": [[float(v), 0.0] for v in x],
                  "elements": [[i, i + 1] for i in range(4)],
                  "supports": [{"node": 0, "type": "pin"}, {"node": 4, "type": "roller"}]},
        "load_cases": [{"point_loads": [{"node": 2, "fy": -5e4}]}],
        "task_params": {"model": "model.pt"},
    }))
    assert main(["run", str(pr), str(tmp_path / "r3.json")]) == 0
    r3 = json.loads((tmp_path / "r3.json").read_text())
    assert r3["ok"] and len(r3["I"]) == 4 and len(r3["cases"]) == 1
