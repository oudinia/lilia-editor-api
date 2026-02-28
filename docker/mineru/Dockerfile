# MinerU 2.7+ with VLM backend (GPU required)
# Requires: NVIDIA GPU with 10GB+ VRAM, CUDA 12.8+, --gpus all
# Based on the official MinerU Docker image:
# https://opendatalab.github.io/MinerU/quick_start/docker_deployment/

FROM vllm/vllm-openai:v0.10.1.1

RUN apt-get update && \
    apt-get install -y \
        fonts-noto-core \
        fonts-noto-cjk \
        fontconfig \
        libgl1 \
        curl && \
    fc-cache -fv && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

RUN python3 -m pip install -U "mineru[core]>=2.7.0" --break-system-packages && \
    python3 -m pip cache purge

# Download all models (pipeline + VLM)
RUN mineru-models-download -s huggingface -m all

ENV MINERU_MODEL_SOURCE=local

EXPOSE 8000

HEALTHCHECK --interval=30s --timeout=10s --start-period=180s --retries=5 \
  CMD curl -f http://localhost:8000/docs || exit 1

ENTRYPOINT ["mineru-api", "--host", "0.0.0.0", "--port", "8000"]
