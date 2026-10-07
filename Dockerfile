FROM mcr.microsoft.com/dotnet/sdk:10.0.103 AS build
WORKDIR /src

# Copy everything
COPY src/ src/
COPY Lilia.Api.slnx .

# Workaround for .NET 10 SDK bug (dotnet/msbuild#12546):
# Glob expansion for **/*.resx fails because MSBuild tries to traverse
# bin/Debug which doesn't exist when building in Release mode.
RUN mkdir -p src/Lilia.Api/bin/Debug
RUN dotnet publish src/Lilia.Api/Lilia.Api.csproj -c Release -o /app/publish

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0.3 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    xz-utils \
    texlive-latex-base \
    texlive-latex-recommended \
    texlive-latex-extra \
    texlive-fonts-recommended \
    texlive-science \
    texlive-plain-generic \
    # TikZ figures: pgf/TikZ, pgfplots and tikz-cd (texlive-pictures; latex-extra pulls it in
    # today, named here because the figures depend on it). standalone and varwidth, which each
    # figure's own compile uses, are in texlive-latex-extra; pdftocairo, which turns that PDF
    # into the SVG, is in poppler-utils below.
    texlive-pictures \
    # biblatex + Biber backend (GA: the biblatex citation export) and the
    # publisher journal classes (IEEEtran, elsarticle, llncs, …) so
    # ?citationBackend=biblatex and journal-class documents also compile
    # in-app, not only when the user compiles elsewhere (Overleaf/arXiv).
    texlive-bibtex-extra \
    biber \
    texlive-publishers \
    lmodern \
    cm-super \
    dvipng \
    ghostscript \
    poppler-utils \
    # XeLaTeX + LuaLaTeX engines — added for #23. texlive-xetex and
    # texlive-luatex bring the compilers; the extra font packages are
    # the common font-spec requirements for CJK / RTL / OpenType users.
    texlive-xetex \
    texlive-luatex \
    fonts-liberation \
    fonts-dejavu-core \
    fonts-noto-core \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Document themes (lilia-theme.sty, Document settings -> Look). Cerulean and Index set their type in
# Montserrat over Source Serif; Carnet and Gazette add EB Garamond and Josefin. Debian and
# Ubuntu ship these only inside texlive-fonts-extra (over a gigabyte), so just these TeX Live
# packages are fetched from the TeX Live repository and unpacked into TEXMFLOCAL: the four font
# packages, mweights (which they load) and ly1 (montserrat loads the LY1 encoding). Their Type 1
# maps are enabled for pdfTeX and LuaTeX. The kpsewhich at the end fails the build if anything is
# missing; at runtime GET /api/themes reports a theme unavailable, and it is never compiled, when its
# fonts are not found, so a theme never prints in a substitute face.
# One fixed mirror: mirrors.ctan.org redirects to a random one, and one with a broken certificate
# failed a build. Override with --build-arg CTAN_MIRROR=... if it is down.
ARG CTAN_MIRROR=https://mirrors.mit.edu/CTAN
RUN set -eux; \
    texmf=/usr/local/share/texmf; \
    mkdir -p "$texmf"; \
    for pkg in montserrat sourceserif ebgaramond josefin mweights ly1; do \
      curl -fsSL --retry 3 "$CTAN_MIRROR/systems/texlive/tlnet/archive/$pkg.tar.xz" | tar -xJf - -C "$texmf"; \
    done; \
    rm -rf "$texmf/tlpkg"; \
    mktexlsr "$texmf"; \
    for map in Montserrat.map SourceSerifFour.map EBGaramond.map josefin.map; do \
      updmap-sys --nohash --enable "Map=$map"; \
    done; \
    updmap-sys; \
    kpsewhich montserrat.sty sourceserifpro.sty ebgaramond.sty josefin.sty mweights.sty ly1enc.def fontaxes.sty

# Install Typst
RUN curl -sSL https://github.com/typst/typst/releases/latest/download/typst-x86_64-unknown-linux-musl.tar.xz \
    | tar xJf - -C /usr/local/bin --strip-components=1

# Compilation worker pool concurrency (scale via instance_count in .do/app.yaml)
ENV COMPILATION_MAX_CONCURRENT=5

RUN groupadd --system appgroup && useradd --system --gid appgroup appuser

COPY --from=build /app/publish .
COPY scripts/precompile-preamble.sh /app/scripts/precompile-preamble.sh
RUN chmod +x /app/scripts/precompile-preamble.sh && /app/scripts/precompile-preamble.sh

RUN mkdir -p /app/uploads /app/logs && chown -R appuser:appgroup /app/uploads /app/logs

USER appuser

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Lilia.Api.dll"]
