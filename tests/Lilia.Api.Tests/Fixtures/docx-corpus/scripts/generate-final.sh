#!/usr/bin/env bash
# Final batch — push past 100 with a few more variants.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/../generated"

emit() {
  local name=$1 md=$2
  echo "$md" | pandoc -f markdown -t docx -o "$OUT/$name.docx"
  echo "  generated $name.docx"
}

emit "minutes-board" "# Board Meeting Minutes — April 22, 2026

**Present:** Chair, CEO, CFO, three directors

**Apologies:** One director (out of country)

## 1. Approval of previous minutes

The minutes of the March 18 meeting were approved unanimously.

## 2. Financial review

The CFO presented Q1 results: revenue \$8.2M (+18% YoY), profit \$1.1M (+25% YoY).

## 3. Strategic review

The CEO outlined the proposed expansion into European markets. Discussion focused on regulatory considerations and hiring plans.

## 4. Resolutions

- Approve European expansion plan
- Approve revised compensation framework
- Authorise Series B fundraising"

emit "shareholder-letter" "# Letter to Shareholders — Q2 2026

Dear shareholders,

Q2 was our strongest quarter to date. Revenue grew 22% YoY and we expanded to two new countries.

## What worked

- Enterprise sales motion has matured
- Customer retention reached 95%
- Product velocity increased with new architecture

## What didn't

- Mobile launch delayed by one quarter
- Some pipeline opportunities pushed to Q3

## Looking ahead

We're investing in the platform layer to support the next phase of growth.

Sincerely,

CEO"

emit "service-runbook" "# Service Runbook: payment-api

## Overview

The payment-api handles all payment authorisation and capture for the platform.

## Dependencies

- Stripe (primary)
- PayPal (secondary)
- Internal user-service
- Postgres (transaction log)

## On-call procedures

### High error rate

1. Check Datadog dashboard
2. If Stripe is down, failover to PayPal
3. Page Stripe support if outage exceeds 15 minutes

### Slow responses

1. Check database connection pool
2. Check downstream API latencies
3. Scale horizontally if traffic spike

### Failed deployments

1. Initiate rollback via cloud-cli
2. Verify metrics return to baseline
3. Investigate root cause"

emit "user-research-report" "# User Research Report: Onboarding Friction

## Method

Conducted 15 user interviews and analysed onboarding completion data for 1,200 users.

## Key findings

1. **Step 4 has the highest dropout** (32% abandonment rate)
2. Users misunderstand the \"verify\" prompt
3. Mobile users complete onboarding 40% less than desktop

## Quotes

> \"I wasn't sure if I needed to do that step before continuing.\"

> \"On my phone the form was hard to fill out.\"

## Recommendations

1. Redesign step 4 with clearer guidance
2. Make verification optional with reminder
3. Mobile-first form design"

emit "feature-spec" "# Feature Spec: Bulk Document Export

## Goals

Allow users to export multiple documents in a single download.

## Requirements

- Select multiple documents in the document list
- Export options: PDF, DOCX, ZIP of individual files
- Async processing for >10 documents
- Email notification when ready

## Non-requirements

- Custom templates (separate spec)
- Scheduled exports (separate spec)

## UX

A bulk action menu appears when multiple documents are selected. Clicking \"Export\" opens a modal to choose format.

## Backend

POST /api/exports with array of document IDs and format. Returns an export job ID. Job status pollable via GET /api/exports/{id}.

## Migration

No migration required — purely additive feature."

emit "user-manual-section" "# Section 4: Managing Documents

## Creating a document

Click the **New Document** button in the top toolbar. Choose a template or start from scratch.

## Editing

Click any block to edit it. Use the **+** button to insert a new block above or below.

## Saving

Documents auto-save every 30 seconds. You can also press **Ctrl+S** (or **Cmd+S** on Mac) to save manually.

## Sharing

Click the **Share** button to invite collaborators or generate a public link.

## Deleting

To delete a document, click the **...** menu and select **Delete**. Deleted documents are kept in trash for 30 days."

emit "specification-document" "# Specification: Public API v3

## Endpoint conventions

All endpoints follow REST principles:

- GET for retrieval
- POST for creation
- PUT for full update
- PATCH for partial update
- DELETE for removal

## Authentication

All requests require a Bearer token in the Authorization header.

## Rate limits

- 100 requests per minute per token (default)
- 1,000 requests per minute for paid tiers

## Error format

Errors return JSON with the following shape:

\`\`\`json
{
  \"error\": {
    \"code\": \"VALIDATION_ERROR\",
    \"message\": \"The 'name' field is required.\"
  }
}
\`\`\`

## Versioning

API version is specified via the Accept header:

\`\`\`
Accept: application/vnd.example.v3+json
\`\`\`"

total=$(ls "$OUT"/*.docx 2>/dev/null | wc -l)
echo ""
echo "Total: $total"
