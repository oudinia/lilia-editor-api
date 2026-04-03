FROM debian:bookworm-slim
RUN apt-get update -qq && apt-get install -y -qq --no-install-recommends \
    texlive-latex-extra texlive-fonts-recommended texlive-science \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /work
COPY validate.sh /usr/local/bin/validate
RUN chmod +x /usr/local/bin/validate
EXPOSE 8088
CMD ["validate"]
