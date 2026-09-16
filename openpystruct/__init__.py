"""OpenPyStruct: OpenSees + PyTorch structural optimization with ML surrogates.

The package is the engine behind the Grasshopper plugin in ``grasshopper/``. The plugin writes a
``case.json`` (see :mod:`openpystruct.schema`), runs ``openpystruct run case.json result.json``
inside a container, and reads ``result.json`` back. The original research scripts at the repo
root remain untouched; this package factors their physics and models into importable, testable
pieces.
"""

from .schema import CASE_SCHEMA, RESULT_SCHEMA

__version__ = "0.1.0"
__all__ = ["CASE_SCHEMA", "RESULT_SCHEMA", "__version__"]
