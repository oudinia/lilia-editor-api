FROM python:3.11-slim

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    libgl1 libglib2.0-0 curl && \
    rm -rf /var/lib/apt/lists/*

RUN pip install --no-cache-dir "mineru[core]"

# Download models (takes a while but cached in layer)
RUN mineru-models-download -s huggingface -m all

ENV MINERU_MODEL_SOURCE=local

EXPOSE 8000

HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=5 \
  CMD curl -f http://localhost:8000/docs || exit 1

ENTRYPOINT ["mineru-api", "--host", "0.0.0.0", "--port", "8000"]
