"""Closed-form checks of the FE core, on every available backend."""
import numpy as np
import pytest

from openpystruct.beam import beam_load_case, beam_model
from openpystruct.fe import LoadCase, Model, analyze, opensees_available

E, A = 200e9, 0.01


def test_simply_supported_udl_midspan_moment(backend):
    L, w, I = 10.0, -5000.0, 1e-4
    model = beam_model(L, 20, roller_x=[L])
    lc = beam_load_case(model, [], [], udl=w)
    a = analyze(model, lc, E, A, np.full(20, I), backend=backend)
    # M_max = w L^2 / 8 at midspan, sagging positive under a downward load
    mid = 10
    assert a.M_i[mid] == pytest.approx(abs(w) * L * L / 8, rel=1e-6)
    # deflection 5 w L^4 / (384 E I), downward (negative)
    assert a.disp[mid, 1] == pytest.approx(-5 * abs(w) * L**4 / (384 * E * I), rel=1e-6)
    # shear antisymmetric, V(0+) = wL/2
    assert a.V_i[0] == pytest.approx(abs(w) * L / 2, rel=1e-6)
    assert a.V_j[-1] == pytest.approx(-abs(w) * L / 2, rel=1e-6)


def test_cantilever_tip_load(backend):
    L, P, I = 4.0, -10e3, 2e-4
    model = beam_model(L, 8, roller_x=[], pin_x=[], fixed_x=[0.0])
    lc = beam_load_case(model, [L], [P])
    a = analyze(model, lc, E, A, np.full(8, I), backend=backend)
    assert a.M_i[0] == pytest.approx(P * L, rel=1e-6)          # hogging -> negative
    assert a.disp[-1, 1] == pytest.approx(P * L**3 / (3 * E * I), rel=1e-6)
    assert a.V_i[0] == pytest.approx(-P, rel=1e-6)             # M = P(L-x) -> dM/dx = -P
    assert np.allclose(a.N, 0.0, atol=1e-6)


def test_portal_frame_lateral_load_equilibrium(backend):
    # Two columns (0,0)-(0,3), (6,0)-(6,3) and a beam; lateral load at the top-left node.
    nodes = [[0, 0], [0, 3], [6, 3], [6, 0]]
    elements = [[0, 1], [1, 2], [3, 2]]
    supports = [{"node": 0, "type": "fixed"}, {"node": 3, "type": "fixed"}]
    model = Model(np.array(nodes, float), np.array(elements), supports)
    lc = LoadCase(point_loads=[{"node": 1, "fx": 1e4, "fy": 0, "mz": 0}],
                  element_loads=[{"element": 1, "wy": -2e3}])
    a = analyze(model, lc, E, A, np.full(3, 1e-4), backend=backend)
    # column local y points -x (columns run +y), so a +Fx sway reads as +V at the bases
    base_shear = a.V_i[0] + a.V_i[2]
    assert base_shear == pytest.approx(1e4, rel=1e-6)
    # vertical reactions balance the beam UDL; columns are in compression (tension positive)
    assert a.N[0] + a.N[2] == pytest.approx(-2e3 * 6, rel=1e-6)
    assert a.disp[1, 0] > 0  # sways in the load direction


@pytest.mark.skipif(not opensees_available(), reason="openseespy not installed")
def test_backends_agree():
    nodes = [[0, 0], [0, 3], [5, 3.5], [5, 0], [10, 3]]
    elements = [[0, 1], [1, 2], [3, 2], [2, 4]]
    supports = [{"node": 0, "type": "fixed"}, {"node": 3, "type": "pin"}, {"node": 4, "type": "roller"}]
    model = Model(np.array(nodes, float), np.array(elements), supports)
    lc = LoadCase(point_loads=[{"node": 1, "fx": 5e3, "fy": -1e4, "mz": 2e3}],
                  element_loads=[{"element": 1, "wy": -3e3}, {"element": 3, "wy": -1e3}])
    I = np.array([1e-4, 2e-4, 1.5e-4, 3e-4])
    a = analyze(model, lc, E, A, I, backend="numpy")
    b = analyze(model, lc, E, A, I, backend="opensees")
    for name in ("disp", "N", "V_i", "V_j", "M_i", "M_j"):
        np.testing.assert_allclose(getattr(a, name), getattr(b, name), rtol=1e-6, atol=1e-6)


def test_mechanism_is_reported():
    model = beam_model(5.0, 4, roller_x=[], pin_x=[])  # no supports at all
    model.supports = [{"node": 0, "type": "roller"}]  # still a mechanism
    with pytest.raises(RuntimeError, match="mechanism"):
        analyze(model, beam_load_case(model, [2.5], [-1.0]), E, A, np.full(4, 1e-4), backend="numpy")
