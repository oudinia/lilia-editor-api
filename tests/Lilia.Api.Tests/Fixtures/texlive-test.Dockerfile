# Test toolchain for LatexIntegrationTests.
#
# These 55 tests compile every block type with a real TeX engine, and they need
# an image whose package set matches what production installs. Two obvious
# candidates are both wrong:
#
#   texlive/texlive:latest   a full TeX Live, ~5GB. It is a *superset* of
#                            production, so a test can pass on a package the
#                            deployed container does not have — and pulling it
#                            fails outright on a constrained network.
#
#   lilia-texlive:bookworm   the latex-service's measurement toolchain, already
#                            on developer machines. Measured against our
#                            preamble it lacks exactly three packages:
#                            siunitx, algorithm, algorithmic.
#
# So this is the second one plus those three. It builds in a minute or two
# instead of downloading five gigabytes, and it is closer to production than
# the full image is.
#
#   docker build -f tests/Lilia.Api.Tests/Fixtures/texlive-test.Dockerfile \
#                -t lilia-texlive:test .
#   LILIA_TEX_IMAGE=lilia-texlive:test dotnet test
#
# If the base image is ever rebuilt with a different package set, re-run the
# check that produced this list rather than trusting it — it was measured, not
# read off a manifest.
FROM lilia-texlive:bookworm

USER root

# texlive-science carries siunitx and the algorithms bundle (algorithm.sty,
# algorithmic.sty). --no-install-recommends keeps this to the packages named
# rather than pulling a large part of TeX Live back in.
RUN apt-get update \
    && apt-get install -y --no-install-recommends texlive-science \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

USER tex
