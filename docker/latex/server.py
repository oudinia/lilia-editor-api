"""
LaTeX Rendering Service
Converts LaTeX source to PDF, PNG, or SVG.
"""

import os
import subprocess
import tempfile
import shutil
import time
import logging
from pathlib import Path
from fastapi import FastAPI, HTTPException
from fastapi.responses import Response, JSONResponse
from pydantic import BaseModel, Field

app = FastAPI(title="Lilia LaTeX Renderer", version="1.0.0")
logger = logging.getLogger("latex-renderer")
logging.basicConfig(level=logging.INFO)

# Verify pdflatex is available
PDFLATEX = shutil.which("pdflatex")
DVIPNG = shutil.which("dvipng")
PDF2SVG = shutil.which("pdf2svg")
PDFTOPPM = shutil.which("pdftoppm")

if not PDFLATEX:
    logger.error("pdflatex not found in PATH")


# --- Models ---

class RenderRequest(BaseModel):
    latex: str = Field(..., description="Full LaTeX document source")
    dpi: int = Field(default=150, ge=72, le=600, description="DPI for PNG output")
    timeout: int = Field(default=30, ge=5, le=120, description="Compilation timeout in seconds")


class RenderBlockRequest(BaseModel):
    latex: str = Field(..., description="LaTeX fragment (will be wrapped in a minimal document)")
    preamble: str = Field(default="", description="Additional preamble packages/commands")
    dpi: int = Field(default=150, ge=72, le=600)
    timeout: int = Field(default=15, ge=5, le=60)


# --- LaTeX compilation ---

MINIMAL_PREAMBLE = r"""\documentclass[preview,border=2pt]{standalone}
\usepackage[utf8]{inputenc}
\usepackage[T1]{fontenc}
\usepackage{amsmath,amssymb,amsfonts}
\usepackage{mathtools}
\usepackage{physics}
\usepackage{bm}
\usepackage{graphicx}
\usepackage{xcolor}
\usepackage{booktabs}
\usepackage{listings}
\usepackage{hyperref}
"""


def compile_latex(latex_source: str, timeout: int = 30) -> tuple[bytes, str]:
    """
    Compile LaTeX source to PDF.
    Returns (pdf_bytes, log_output).
    Raises HTTPException on failure.
    """
    with tempfile.TemporaryDirectory(prefix="lilia-latex-") as tmpdir:
        tex_path = os.path.join(tmpdir, "document.tex")
        pdf_path = os.path.join(tmpdir, "document.pdf")
        log_path = os.path.join(tmpdir, "document.log")

        # Write source
        with open(tex_path, "w", encoding="utf-8") as f:
            f.write(latex_source)

        # Run pdflatex twice (for references)
        for pass_num in range(2):
            try:
                result = subprocess.run(
                    [
                        PDFLATEX,
                        "-interaction=nonstopmode",
                        "-halt-on-error",
                        "-output-directory", tmpdir,
                        tex_path,
                    ],
                    capture_output=True,
                    text=True,
                    timeout=timeout,
                    cwd=tmpdir,
                )
            except subprocess.TimeoutExpired:
                raise HTTPException(
                    status_code=408,
                    detail=f"LaTeX compilation timed out after {timeout}s"
                )

            if result.returncode != 0 and pass_num == 1:
                # Extract error from log
                log_content = ""
                if os.path.exists(log_path):
                    with open(log_path, "r", errors="replace") as f:
                        log_content = f.read()

                # Find the error line
                error_lines = []
                for line in log_content.split("\n"):
                    if line.startswith("!") or "Error" in line:
                        error_lines.append(line)

                error_msg = "\n".join(error_lines[:5]) if error_lines else result.stderr[:500]
                raise HTTPException(
                    status_code=422,
                    detail=f"LaTeX compilation failed:\n{error_msg}"
                )

        if not os.path.exists(pdf_path):
            raise HTTPException(status_code=500, detail="PDF was not generated")

        with open(pdf_path, "rb") as f:
            pdf_bytes = f.read()

        log_content = ""
        if os.path.exists(log_path):
            with open(log_path, "r", errors="replace") as f:
                log_content = f.read()

        return pdf_bytes, log_content


def pdf_to_png(pdf_bytes: bytes, dpi: int = 150) -> bytes:
    """Convert PDF to PNG using pdftoppm."""
    with tempfile.TemporaryDirectory(prefix="lilia-png-") as tmpdir:
        pdf_path = os.path.join(tmpdir, "input.pdf")
        with open(pdf_path, "wb") as f:
            f.write(pdf_bytes)

        output_prefix = os.path.join(tmpdir, "output")
        try:
            subprocess.run(
                [PDFTOPPM, "-png", "-r", str(dpi), "-singlefile", pdf_path, output_prefix],
                capture_output=True,
                timeout=10,
            )
        except subprocess.TimeoutExpired:
            raise HTTPException(status_code=408, detail="PNG conversion timed out")

        png_path = output_prefix + ".png"
        if not os.path.exists(png_path):
            raise HTTPException(status_code=500, detail="PNG conversion failed")

        with open(png_path, "rb") as f:
            return f.read()


def pdf_to_svg(pdf_bytes: bytes) -> str:
    """Convert PDF to SVG using pdf2svg."""
    with tempfile.TemporaryDirectory(prefix="lilia-svg-") as tmpdir:
        pdf_path = os.path.join(tmpdir, "input.pdf")
        svg_path = os.path.join(tmpdir, "output.svg")

        with open(pdf_path, "wb") as f:
            f.write(pdf_bytes)

        try:
            subprocess.run(
                [PDF2SVG, pdf_path, svg_path],
                capture_output=True,
                timeout=10,
            )
        except subprocess.TimeoutExpired:
            raise HTTPException(status_code=408, detail="SVG conversion timed out")

        if not os.path.exists(svg_path):
            raise HTTPException(status_code=500, detail="SVG conversion failed")

        with open(svg_path, "r") as f:
            return f.read()


# --- Endpoints ---

@app.get("/health")
async def health():
    return {
        "status": "ok",
        "pdflatex": PDFLATEX is not None,
        "dvipng": DVIPNG is not None,
        "pdf2svg": PDF2SVG is not None,
    }


@app.get("/docs")
async def docs_redirect():
    """Redirect to FastAPI docs (for DO health check compatibility)."""
    from fastapi.responses import RedirectResponse
    return RedirectResponse(url="/docs")


@app.post("/render/pdf")
async def render_pdf(req: RenderRequest):
    """Render full LaTeX document to PDF."""
    start = time.time()
    pdf_bytes, _ = compile_latex(req.latex, req.timeout)
    elapsed = time.time() - start
    logger.info(f"PDF rendered in {elapsed:.2f}s ({len(pdf_bytes)} bytes)")
    return Response(content=pdf_bytes, media_type="application/pdf", headers={
        "X-Render-Time": f"{elapsed:.3f}",
    })


@app.post("/render/png")
async def render_png(req: RenderRequest):
    """Render full LaTeX document to PNG."""
    start = time.time()
    pdf_bytes, _ = compile_latex(req.latex, req.timeout)
    png_bytes = pdf_to_png(pdf_bytes, req.dpi)
    elapsed = time.time() - start
    logger.info(f"PNG rendered in {elapsed:.2f}s ({len(png_bytes)} bytes)")
    return Response(content=png_bytes, media_type="image/png", headers={
        "X-Render-Time": f"{elapsed:.3f}",
    })


@app.post("/render/svg")
async def render_svg(req: RenderRequest):
    """Render full LaTeX document to SVG."""
    start = time.time()
    pdf_bytes, _ = compile_latex(req.latex, req.timeout)
    svg_content = pdf_to_svg(pdf_bytes)
    elapsed = time.time() - start
    logger.info(f"SVG rendered in {elapsed:.2f}s")
    return Response(content=svg_content, media_type="image/svg+xml", headers={
        "X-Render-Time": f"{elapsed:.3f}",
    })


@app.post("/render/block")
async def render_block(req: RenderBlockRequest):
    """Render a LaTeX fragment (equation, table, etc.) wrapped in a standalone document."""
    full_source = MINIMAL_PREAMBLE
    if req.preamble:
        full_source += req.preamble + "\n"
    full_source += "\\begin{document}\n"
    full_source += req.latex + "\n"
    full_source += "\\end{document}\n"

    start = time.time()
    pdf_bytes, _ = compile_latex(full_source, req.timeout)
    png_bytes = pdf_to_png(pdf_bytes, req.dpi)
    elapsed = time.time() - start
    logger.info(f"Block rendered in {elapsed:.2f}s ({len(png_bytes)} bytes)")
    return Response(content=png_bytes, media_type="image/png", headers={
        "X-Render-Time": f"{elapsed:.3f}",
    })


@app.post("/validate")
async def validate_latex(req: RenderRequest):
    """Validate LaTeX source without returning the PDF."""
    start = time.time()
    try:
        _, log_content = compile_latex(req.latex, req.timeout)
        elapsed = time.time() - start

        # Extract warnings
        warnings = []
        for line in log_content.split("\n"):
            if "Warning" in line or "Underfull" in line or "Overfull" in line:
                warnings.append(line.strip())

        return JSONResponse({
            "valid": True,
            "warnings": warnings[:20],
            "renderTime": round(elapsed, 3),
        })
    except HTTPException as e:
        elapsed = time.time() - start
        return JSONResponse({
            "valid": False,
            "error": e.detail,
            "renderTime": round(elapsed, 3),
        }, status_code=200)  # 200 even on invalid — it's a validation response


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8001)
