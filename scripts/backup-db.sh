#!/bin/bash
# =============================================================================
# Lilia Database Backup Script
# =============================================================================
# Daily pg_dump of the DigitalOcean managed PostgreSQL database.
# Keeps local backups with configurable retention, optionally uploads to DO Spaces.
#
# Usage:
#   ./backup-db.sh                    # Uses environment variables
#   ./backup-db.sh --env /path/.env   # Loads from .env file
#
# Environment variables:
#   DB_HOST        - PostgreSQL host (required)
#   DB_PORT        - PostgreSQL port (default: 25060 for DO managed DB)
#   DB_NAME        - Database name (default: lilia)
#   DB_USER        - Database user (default: doadmin)
#   DB_PASSWORD    - Database password (required)
#   DB_SSLMODE     - SSL mode (default: require)
#   BACKUP_DIR     - Local backup directory (default: /var/backups/lilia)
#   RETENTION_DAYS - Days to keep local backups (default: 30)
#   DO_SPACES_BUCKET   - DO Spaces bucket name (optional, enables upload)
#   DO_SPACES_REGION   - DO Spaces region (default: fra1)
#
# Cron example (daily at 3 AM):
#   0 3 * * * /opt/lilia/scripts/backup-db.sh >> /var/log/lilia-backup.log 2>&1
#
# =============================================================================

set -euo pipefail

# Load .env file if specified
if [[ "${1:-}" == "--env" ]] && [[ -f "${2:-}" ]]; then
    set -a
    source "$2"
    set +a
fi

# Configuration with defaults
DB_HOST="${DB_HOST:?DB_HOST is required}"
DB_PORT="${DB_PORT:-25060}"
DB_NAME="${DB_NAME:-lilia}"
DB_USER="${DB_USER:-doadmin}"
DB_PASSWORD="${DB_PASSWORD:?DB_PASSWORD is required}"
DB_SSLMODE="${DB_SSLMODE:-require}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/lilia}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"
DO_SPACES_BUCKET="${DO_SPACES_BUCKET:-}"
DO_SPACES_REGION="${DO_SPACES_REGION:-fra1}"

TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="${BACKUP_DIR}/lilia_${TIMESTAMP}.dump"
LOG_PREFIX="[$(date '+%Y-%m-%d %H:%M:%S')]"

echo "${LOG_PREFIX} Starting database backup..."

# Create backup directory
mkdir -p "$BACKUP_DIR"

# Run pg_dump (custom format = compressed + supports selective pg_restore)
echo "${LOG_PREFIX} Dumping ${DB_NAME}@${DB_HOST}:${DB_PORT}..."
PGPASSWORD="$DB_PASSWORD" pg_dump \
    -h "$DB_HOST" \
    -p "$DB_PORT" \
    -U "$DB_USER" \
    -d "$DB_NAME" \
    --no-owner \
    --no-acl \
    --format=custom \
    --compress=9 \
    -f "$BACKUP_FILE" \
    2>&1

FILESIZE=$(du -h "$BACKUP_FILE" | cut -f1)
echo "${LOG_PREFIX} Backup created: ${BACKUP_FILE} (${FILESIZE})"

# Prune old local backups
DELETED=$(find "$BACKUP_DIR" -name "lilia_*.dump" -mtime +"$RETENTION_DAYS" -print -delete | wc -l)
if [[ "$DELETED" -gt 0 ]]; then
    echo "${LOG_PREFIX} Pruned ${DELETED} backup(s) older than ${RETENTION_DAYS} days"
fi

# Upload to DO Spaces (if configured)
if [[ -n "$DO_SPACES_BUCKET" ]]; then
    SPACES_ENDPOINT="https://${DO_SPACES_REGION}.digitaloceanspaces.com"
    REMOTE_PATH="s3://${DO_SPACES_BUCKET}/backups/db/$(basename "$BACKUP_FILE")"

    echo "${LOG_PREFIX} Uploading to DO Spaces: ${REMOTE_PATH}..."

    if command -v s3cmd &>/dev/null; then
        s3cmd put "$BACKUP_FILE" "$REMOTE_PATH" \
            --host="${DO_SPACES_REGION}.digitaloceanspaces.com" \
            --host-bucket="%(bucket)s.${DO_SPACES_REGION}.digitaloceanspaces.com" \
            2>&1
    elif command -v aws &>/dev/null; then
        aws s3 cp "$BACKUP_FILE" "$REMOTE_PATH" \
            --endpoint-url "$SPACES_ENDPOINT" \
            2>&1
    else
        echo "${LOG_PREFIX} WARNING: Neither s3cmd nor aws CLI found — skipping Spaces upload"
    fi

    echo "${LOG_PREFIX} Upload complete"
fi

# Summary
BACKUP_COUNT=$(find "$BACKUP_DIR" -name "lilia_*.dump" | wc -l)
TOTAL_SIZE=$(du -sh "$BACKUP_DIR" | cut -f1)
echo "${LOG_PREFIX} Done. ${BACKUP_COUNT} backup(s) on disk, total ${TOTAL_SIZE}"
