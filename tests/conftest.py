import pytest

from openpystruct.fe import opensees_available

BACKENDS = ["numpy"] + (["opensees"] if opensees_available() else [])


@pytest.fixture(params=BACKENDS)
def backend(request):
    return request.param
