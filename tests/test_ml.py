import json

import numpy as np
import pytest

from openpystruct.beam import beam_load_case, beam_model
from openpystruct.datagen import generate
from openpystruct.ml.features import FeatureSpec, Scaler, build_training_arrays, unify_label_with_c
from openpystruct.ml.predict import predict
from openpystruct.ml.train import train
from openpystruct.schema import DEFAULT_MATERIAL, DEFAULT_OPTIMIZER

FAST_OPT = dict(DEFAULT_OPTIMIZER, epochs=5, patience=100)
GEN = {"num_samples": 12, "workers": 1, "seed": 1, "length": 30.0, "n_elements": 6,
       "roller_x": [10.0, 30.0], "max_forces": 2, "udl": -500.0}


def test_scaler_roundtrip():
    x = np.random.rand(20, 4).astype(np.float32) * 10
    s = Scaler.fit(x)
    np.testing.assert_allclose(s.inverse(s.transform(x)), x, rtol=1e-5)
    s2 = Scaler.from_json(json.loads(json.dumps(s.to_json())))
    np.testing.assert_allclose(s2.transform(x), s.transform(x))


def test_unify_label():
    I = np.array([[[1, 2], [3, 4]]], float)  # (1, 2 cases, 2 elems)
    np.testing.assert_allclose(unify_label_with_c(I, 0.0), [[2, 3]])
    np.testing.assert_allclose(unify_label_with_c(I, 1.0), [[3, 4]])


def test_generate_dataset_shape():
    data = generate(GEN, DEFAULT_MATERIAL, FAST_OPT, backend="numpy")
    assert len(data["I_values"]) == 12
    assert all(len(r) == 6 for r in data["I_values"])
    assert all(len(r) == 7 for r in data["node_positions"])
    assert data["roller_x_locations"][0] == [10.0, 30.0]
    assert 1 <= len(data["force_values"][0]) <= 2


def test_build_training_arrays_layout():
    data = generate(GEN, DEFAULT_MATERIAL, FAST_OPT, backend="numpy")
    X, Y, spec, extras = build_training_arrays(data, n_cases=3, c=1.0, with_physics=True)
    assert X.shape == (4, 3 * spec.per_case_dim)
    assert Y.shape == (4, 6 + 7 + 7)
    assert extras["nelem"] == 6
    spec2 = FeatureSpec.from_json(json.loads(json.dumps(spec.to_json())))
    assert spec2.input_dim == X.shape[1]


@pytest.mark.parametrize("kind", ["fnn", "pinn"])
def test_train_then_predict(tmp_path, kind):
    data = generate(GEN, DEFAULT_MATERIAL, FAST_OPT, backend="numpy")
    ds = tmp_path / "dataset.json"
    ds.write_text(json.dumps(data))
    res = train({"dataset": "dataset.json", "kind": kind, "output": "model.pt", "n_cases": 3,
                 "epochs": 3, "batch_size": 2, "hidden_units": 16, "num_blocks": 1},
                str(tmp_path))
    assert (tmp_path / "model.pt").exists()
    assert res["epochs"] >= 1 and res["nelem"] == 6

    model = beam_model(30.0, 6, roller_x=[10.0, 30.0])
    lcs = [beam_load_case(model, [5.0], [-1e5], udl=-500.0), beam_load_case(model, [20.0], [-2e5])]
    out = predict({"model": "model.pt"}, str(tmp_path), model, lcs, DEFAULT_MATERIAL, backend="numpy")
    assert len(out["I"]) == 6 and min(out["I"]) > 0
    assert len(out["cases"]) == 2
    assert (out["predicted_deflections"] is not None) == (kind == "pinn")

    wrong = beam_model(30.0, 5, roller_x=[10.0])
    with pytest.raises(ValueError, match="predicts 6 elements"):
        predict({"model": "model.pt"}, str(tmp_path), wrong, lcs, DEFAULT_MATERIAL, backend="numpy")
