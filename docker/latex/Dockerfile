# LaTeX rendering service — converts LaTeX source to PDF/PNG
# Lightweight Python API wrapping pdflatex + dvipng
#
# Usage:
#   POST /render/pdf   { "latex": "..." } → PDF bytes
#   POST /render/png   { "latex": "...", "dpi": 300 } → PNG bytes
#   POST /render/svg   { "latex": "..." } → SVG string
#   GET  /health       → { "status": "ok" }

FROM python:3.12-slim

# Install TeX Live with common academic packages
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        texlive-latex-base \
        texlive-latex-recommended \
        texlive-latex-extra \
        texlive-fonts-recommended \
        texlive-fonts-extra \
        texlive-science \
        texlive-bibtex-extra \
        texlive-publishers \
        texlive-plain-generic \
        texlive-luatex \
        lmodern \
        cm-super \
        dvipng \
        ghostscript \
        poppler-utils \
        pdf2svg \
        && apt-get clean \
        && rm -rf /var/lib/apt/lists/*

# Install Python dependencies
RUN pip install --no-cache-dir fastapi uvicorn[standard]

# Copy application
WORKDIR /app
COPY docker/latex/server.py /app/server.py

EXPOSE 8001

# Health check
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8001/health')"

CMD ["uvicorn", "server:app", "--host", "0.0.0.0", "--port", "8001", "--workers", "2"]
