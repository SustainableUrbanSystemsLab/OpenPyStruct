# OpenPyStruct engine image: OpenSeesPy + PyTorch (CPU) + the openpystruct package.
#
# Build from the repo root (the build context must contain openpystruct/ and pyproject.toml):
#
#   docker build -t openpystruct -f docker/Dockerfile .
#   podman build -t openpystruct -f docker/Dockerfile .
#
# The Grasshopper "OpenPyStruct Engine" component runs exactly this when you point it at the
# repo and press Build. Run a case by hand:
#
#   docker run --rm -v "$PWD/work:/work" openpystruct run /work/case.json /work/result.json
#
# CUDA: use docker/Dockerfile.cuda instead and pass --gpus all (Docker) or
# --device nvidia.com/gpu=all (Podman). Training is the only task that benefits.
#
# Platform: OpenSeesPy publishes Linux wheels for x86_64 only. On Apple Silicon build and run
# with --platform linux/amd64 (emulated, slower) or accept the numpy FE backend, which the
# package falls back to automatically when openseespy does not import.
# Registry-qualified on purpose: Podman's short-name resolution prompts (and hangs without a TTY).
FROM docker.io/library/python:3.11-slim

ENV PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

RUN apt-get update \
 && apt-get install -y --no-install-recommends libgomp1 libgfortran5 \
 && rm -rf /var/lib/apt/lists/*

# CPU-only torch wheel: a fraction of the size of the default CUDA build.
RUN pip install torch --index-url https://download.pytorch.org/whl/cpu

# OpenSeesPy is x86_64-only; on other platforms the package's numpy backend takes over.
RUN pip install openseespy || echo "openseespy not installable on this platform; numpy backend will be used"

WORKDIR /src
COPY pyproject.toml README.md ./
COPY openpystruct ./openpystruct
RUN pip install .

WORKDIR /work
ENTRYPOINT ["openpystruct"]
CMD ["info"]
