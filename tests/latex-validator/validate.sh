#!/bin/bash
# Minimal LaTeX validation HTTP server
# Listens on port 8088, accepts POST with LaTeX body, returns JSON {valid, error}

while true; do
  # Read HTTP request
  RESPONSE=$(mktemp)
  TMPDIR=$(mktemp -d)

  {
    read -r METHOD PATH VERSION
    CONTENT_LENGTH=0
    while IFS= read -r header; do
      header=$(echo "$header" | tr -d '\r')
      [ -z "$header" ] && break
      case "$header" in
        Content-Length:*|content-length:*) CONTENT_LENGTH=${header#*: } ;;
      esac
    done

    # Read body
    if [ "$CONTENT_LENGTH" -gt 0 ] 2>/dev/null; then
      dd bs=1 count="$CONTENT_LENGTH" 2>/dev/null > "$TMPDIR/input.tex"
    fi

    # Compile
    if [ -f "$TMPDIR/input.tex" ]; then
      cd "$TMPDIR"
      RESULT=$(pdflatex -interaction=nonstopmode -halt-on-error input.tex 2>&1)
      EXIT_CODE=$?
      cd /work

      if [ $EXIT_CODE -eq 0 ]; then
        echo '{"valid":true,"error":null}' > "$RESPONSE"
      else
        ERROR=$(echo "$RESULT" | grep '^!' | head -3 | tr '\n' ' ' | sed 's/"/\\"/g')
        echo "{\"valid\":false,\"error\":\"$ERROR\"}" > "$RESPONSE"
      fi
    else
      echo '{"valid":false,"error":"No input"}' > "$RESPONSE"
    fi

    BODY=$(cat "$RESPONSE")
    echo -e "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: ${#BODY}\r\nConnection: close\r\n\r\n$BODY"

    rm -rf "$TMPDIR" "$RESPONSE"
  } | nc -l -p 8088 -q 1
done
