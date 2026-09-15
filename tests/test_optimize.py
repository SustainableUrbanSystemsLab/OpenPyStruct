import numpy as np

from openpystruct.beam import beam_load_case, beam_model
from openpystruct.optimize import optimize
from openpystruct.schema import DEFAULT_MATERIAL, DEFAULT_OPTIMIZER


def _small_case():
    model = beam_model(20.0, 10, roller_x=[8.0, 20.0])
    lc = beam_load_case(model, [4.0, 14.0], [-1e5, -2e5], udl=-1e3)
    return model, lc


def test_optimize_reduces_loss_and_stays_positive(backend):
    model, lc = _small_case()
    opt = dict(DEFAULT_OPTIMIZER, epochs=60, patience=100)
    calls = []
    res = optimize(model, [lc], DEFAULT_MATERIAL, opt, backend=backend,
                   progress=lambda d, t, v: calls.append((d, t, v)))
    hist = res["loss_history"]["total"]
    assert len(hist) == res["epochs"] == 60
    assert hist[-1] < hist[0]
    assert min(res["I"]) >= opt["I_min"]
    assert len(res["I"]) == 10
    assert calls[-1][0] == 60 and calls[-1][1] == 60
    case = res["cases"][0]
    assert len(case["moment_i"]) == 10 and len(case["displacements"]) == 11


def test_early_stopping():
    model, lc = _small_case()
    opt = dict(DEFAULT_OPTIMIZER, epochs=500, tolerance=1e9, patience=3)  # never "improves"
    res = optimize(model, [lc], DEFAULT_MATERIAL, opt, backend="numpy")
    assert res["stopped_early"] and res["epochs"] == 4  # first epoch always "improves" on inf


def test_multi_case_envelope_needs_more_I_than_single():
    model, lc1 = _small_case()
    lc2 = beam_load_case(model, [10.0], [-3e5])
    opt = dict(DEFAULT_OPTIMIZER, epochs=80, patience=100)
    single = optimize(model, [lc1], DEFAULT_MATERIAL, opt, backend="numpy")
    both = optimize(model, [lc1, lc2], DEFAULT_MATERIAL, opt, backend="numpy")
    assert np.sum(both["I"]) > np.sum(single["I"])
    assert len(both["cases"]) == 2


def test_envelope_is_bounded_by_sum_and_single_cases():
    model, lc1 = _small_case()
    lc2 = beam_load_case(model, [10.0], [-3e5])
    opt = dict(DEFAULT_OPTIMIZER, epochs=40, patience=100)
    total = optimize(model, [lc1, lc2], DEFAULT_MATERIAL, opt, backend="numpy")
    env = optimize(model, [lc1, lc2], DEFAULT_MATERIAL, dict(opt, combination="envelope"), backend="numpy")
    single = optimize(model, [lc2], DEFAULT_MATERIAL, opt, backend="numpy")
    assert env["combination"] == "envelope"
    # the envelope never asks for more than the sum, and at least as much as any one case
    assert np.sum(env["I"]) <= np.sum(total["I"]) + 1e-9
    assert np.sum(env["I"]) >= np.sum(single["I"]) - 1e-6


def test_unknown_combination_is_rejected():
    import pytest
    model, lc = _small_case()
    with pytest.raises(ValueError, match="combination"):
        optimize(model, [lc], DEFAULT_MATERIAL, dict(DEFAULT_OPTIMIZER, epochs=1, combination="max"), backend="numpy")
