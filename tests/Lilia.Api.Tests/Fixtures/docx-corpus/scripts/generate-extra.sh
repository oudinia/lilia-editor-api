#!/usr/bin/env bash
# Second batch — push the DOCX corpus closer to 100 with more
# document-type archetypes and content-mix variations.

set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/../generated"

emit() {
  local name=$1 md=$2
  echo "$md" | pandoc -f markdown -t docx -o "$OUT/$name.docx"
  echo "  generated $name.docx"
}

emit "academic-cv" "# Curriculum Vitae

**Name:** Dr. Sample

**Email:** sample@example.edu

## Education

- PhD in Computer Science, Example University, 2020
- MS in Mathematics, State University, 2015
- BS in Physics, Local College, 2013

## Publications

1. Sample et al., \"On the structure of distributed systems\", *Nature Computing*, 2024.
2. Sample, \"Approximation algorithms for graphs\", *JACM*, 2023.

## Awards

- Best Paper Award, ACM Symposium 2024
- Early Career Researcher Award, 2023"

emit "research-paper-mixed" "# Reinforcement Learning at Scale

## Abstract

We propose a distributed RL framework that scales to 10{,}000 actors with sublinear communication overhead.

## 1. Introduction

Reinforcement learning has reached human-level performance on many tasks. Scaling beyond single-machine setups remains challenging.

## 2. Related Work

Prior work [1, 2] established the basic asynchronous actor-learner pattern.

## 3. Method

Our key insight is that gradient compression combined with hierarchical aggregation reduces communication by an order of magnitude.

| Method | Speedup |
|---|---|
| Baseline | 1x |
| Compress | 4x |
| Hierarchical | 8x |
| Combined | 12x |

## 4. Conclusion

Distributed RL benefits substantially from communication-aware system design."

emit "policy-document" "# Acceptable Use Policy

**Effective:** April 25, 2026

## 1. Purpose

This policy governs the use of company computing resources by employees and contractors.

## 2. Scope

Applies to all systems owned, leased, or controlled by the company.

## 3. Acceptable Use

Resources may be used for:

- Business activities
- Reasonable personal use that does not interfere with work
- Professional development

## 4. Prohibited Activities

- Sharing credentials with third parties
- Installing unapproved software
- Accessing systems beyond authorisation
- Storing personal media on company systems

## 5. Enforcement

Violations may result in disciplinary action, up to and including termination."

emit "incident-postmortem" "# Incident Postmortem: API Outage 2026-04-23

## Summary

A 47-minute outage of the customer-facing API affected approximately 12% of customers between 14:32 and 15:19 UTC on April 23, 2026.

## Timeline

- 14:30 — Routine deployment of api v3.2.1
- 14:32 — Error rate spikes to 100% in eu-west region
- 14:35 — On-call engineer paged
- 14:42 — Root cause identified: misconfigured connection pool
- 14:55 — Rollback initiated
- 15:19 — Full recovery

## Root Cause

The deployed config reduced the database connection pool from 200 to 20, exhausting connections under normal load.

## Impact

- 47 minutes of degraded service
- ~12% of customers affected (eu-west region)
- ~2.3M failed API requests

## Action Items

- [ ] Add connection pool size validation to deployment pipeline
- [ ] Improve rollback automation
- [ ] Add load test for connection exhaustion scenarios"

emit "design-doc" "# Design Doc: New Auth Service

**Author:** Engineering team

**Status:** Draft

## Goals

- Replace legacy auth service
- Support OAuth2 and SAML
- Reduce p99 latency to under 100ms

## Non-goals

- Replacing the user database (separate project)
- Multi-tenant SSO (future quarter)

## Architecture

The new service is a stateless validator backed by a Redis cache. Token validation happens via JWT signature verification with key rotation.

## Trade-offs

We chose JWT for stateless validation despite revocation complexity. Alternative: opaque tokens with central lookup (more secure, slower).

## Rollout plan

1. Deploy to staging
2. Run shadow traffic for 2 weeks
3. Canary deploy to 1% of production
4. Gradual rollout to 100%
5. Decommission legacy service"

emit "qa-test-plan" "# Test Plan: Checkout Flow Redesign

## Scope

Cover the new checkout flow end-to-end, including:

- Cart review
- Address entry
- Payment selection
- Order confirmation

## Test cases

| ID | Description | Priority |
|---|---|---|
| TC-01 | Happy path: single item | P0 |
| TC-02 | Multiple items with discount | P0 |
| TC-03 | Invalid credit card | P1 |
| TC-04 | Expired session mid-checkout | P1 |
| TC-05 | Out-of-stock during payment | P1 |
| TC-06 | Address validation failure | P2 |

## Environment

- Staging: cart-staging.example.com
- Test data in shared test account

## Exit criteria

- All P0 cases pass
- 90% of P1 cases pass
- No new P0 bugs found"

emit "release-checklist" "# Release Checklist v2.5

## Pre-release

- [ ] All tests pass in CI
- [ ] Code review complete
- [ ] Changelog updated
- [ ] Documentation reviewed

## Release

- [ ] Tag the release in git
- [ ] Build production artifacts
- [ ] Deploy to staging
- [ ] Smoke test staging
- [ ] Deploy to production
- [ ] Smoke test production

## Post-release

- [ ] Monitor error rates for 1 hour
- [ ] Update release announcement
- [ ] Post in #releases channel
- [ ] Send customer-facing notification"

emit "stakeholder-update" "# Q2 Stakeholder Update

## Highlights

- Product launches: 3 (vs 2 planned)
- Customer growth: 28% QoQ
- Revenue: \$3.2M (vs \$2.8M target)

## Key wins

1. Closed largest enterprise deal in company history
2. Expanded into European market
3. Hired VP of Engineering

## Challenges

- Supply chain delays affected two product launches
- Hiring slower than planned in customer success
- Marketing CAC up 12%

## Q3 Priorities

1. Scale enterprise sales motion
2. Launch mobile app
3. Reduce customer onboarding time"

emit "project-status" "# Project Atlas — Status Update

## Milestone progress

- [x] M1: Architecture finalised
- [x] M2: Core API implemented
- [x] M3: Frontend skeleton deployed
- [ ] M4: Beta testing (in progress)
- [ ] M5: Public launch (planned Q3)

## Risks

| Risk | Status | Mitigation |
|---|---|---|
| Vendor delivery slip | Open | Backup vendor identified |
| Performance regression | Resolved | Caching layer added |
| Hiring gap | Open | Two offers extended |

## Next 2 weeks

- Complete beta user onboarding
- Run end-to-end load test
- Finalise launch communication plan"

emit "vendor-evaluation" "# Vendor Evaluation: Payment Processors

## Candidates

We evaluated three payment processors against our criteria.

## Evaluation matrix

| Criterion | Vendor A | Vendor B | Vendor C |
|---|---|---|---|
| Cost | Medium | Low | High |
| Reliability | High | Medium | High |
| API quality | High | High | Medium |
| Geographic coverage | Global | NA only | Global |
| Compliance | Full | Full | Partial |

## Recommendation

Select **Vendor A** based on the balance of cost, reliability, and global coverage. Vendor B is cheaper but lacks international support.

## Next steps

1. Negotiate contract terms
2. Plan integration sprint
3. Phased rollout starting Q3"

emit "interview-prep" "# Senior Engineer Interview Loop

## Format

5 rounds, 60 minutes each, virtual.

## Round 1: Coding (Algorithms)

Problem: Implement a function that finds the longest substring without repeating characters.

Expected approach: sliding window with hashmap, O(n) time, O(min(n, alphabet)) space.

## Round 2: System Design

Problem: Design a URL shortener at scale (Twitter scale).

Expected discussion: hashing strategy, database choice, caching layer, analytics pipeline.

## Round 3: Domain (Distributed Systems)

Discussion topics:

- Consistency models
- Failure detection
- Consensus algorithms

## Round 4: Behavioural

STAR method for past projects.

## Round 5: Bar raiser

Cross-functional collaboration scenario."

emit "weekly-status" "# Engineering Weekly — Week of April 22

## Shipped

- Auth migration complete in eu-west
- New deployment pipeline rolled out to 5 services
- Bug bash resolved 23 issues

## In progress

- Billing module spec — first draft circulated
- Mobile app beta — internal dogfood
- Performance testing for Q3 launch

## Blocked

- SSO integration waiting on vendor API access
- Data migration paused pending compliance review

## Next week

- Finalise billing API spec
- Complete mobile beta feedback round
- Begin Q3 launch readiness review"

emit "rfc-template" "# RFC: Async Job Processing

## Summary

Replace the in-process background job runner with a dedicated queue + worker architecture.

## Motivation

Current setup couples job execution to web servers, causing latency spikes and complicating scaling.

## Detailed Design

### Components

- Queue: Redis Streams
- Workers: stateless worker fleet
- Scheduler: cron-based job scheduling
- Dashboard: visibility into job state

### Data flow

1. Web request enqueues job
2. Worker picks up job
3. Worker executes and reports result
4. Result available via dashboard or callback

## Alternatives considered

- Use Celery + RabbitMQ: too heavy for our scale
- AWS SQS: vendor lock-in
- Custom DB-backed queue: reinventing wheel

## Migration plan

1. Implement queue + workers in parallel
2. Migrate jobs one type at a time
3. Decommission legacy runner

## Open questions

- Do we need exactly-once delivery or at-least-once with idempotency?
- Should the dashboard be in the main app or standalone?"

emit "pitch-deck-text" "# Pitch: Project Aurora

## The problem

Companies waste 40% of their cloud spend on idle resources.

## Our solution

AI-driven workload scheduling that consolidates compute onto fewer instances.

## Market

\$50B annual cloud waste across mid-market and enterprise customers.

## Competition

- Existing tools focus on cost reporting, not active optimisation.
- Hyperscalers offer basic auto-scaling but not cross-workload consolidation.

## Business model

SaaS, priced as a percentage of savings.

## Traction

- 12 design partners
- \$1.2M in saved spend across pilots
- 3 paid contracts in pipeline

## Ask

\$5M Series A to scale go-to-market."

emit "client-proposal" "# Engagement Proposal: Cloud Migration

**Prepared for:** Acme Corporation

**Prepared by:** Consulting Example Ltd

## Executive Summary

We propose a 6-month engagement to migrate Acme's on-premises infrastructure to AWS, with phased cutover to minimise risk.

## Scope

- Lift-and-shift of 80 services
- Refactor of 15 high-priority services
- Decommission of legacy data centre
- Knowledge transfer to in-house team

## Timeline

| Phase | Duration | Outcomes |
|---|---|---|
| Discovery | 4 weeks | Inventory + plan |
| Migration sprint 1 | 8 weeks | 30 services |
| Migration sprint 2 | 8 weeks | 30 services |
| Refactor + cutover | 4 weeks | Critical 15 + DC shutdown |

## Investment

\$450,000 fixed-price.

## Team

- Engagement lead (1)
- Senior engineers (3)
- DevOps specialist (1)

## Next steps

Sign engagement letter by May 15 to start June 1."

emit "marketing-brief" "# Campaign Brief: Spring Product Launch

## Objective

Drive 1,000 trial signups in the first 30 days.

## Target audience

Mid-market technology decision makers, 100-500 employees.

## Key message

\"The only platform that saves you money while making your team more productive.\"

## Channels

- LinkedIn ads
- Industry newsletter sponsorships
- Webinar series (3 events)
- Partner co-marketing

## Budget

\$125,000 across all channels.

## Success metrics

- 1,000 trial signups
- 250 sales-qualified leads
- 25 closed-won deals
- \$500,000 in new ARR"

emit "training-curriculum" "# New Hire Engineering Curriculum

## Week 1: Onboarding

- Day 1: HR + benefits + accounts
- Day 2: Codebase tour + dev environment
- Day 3: Architecture overview
- Day 4: Pair on first PR
- Day 5: Ship first PR

## Week 2: Product Knowledge

- Day 6-7: Product demo and Q&A
- Day 8-9: Customer support shadowing
- Day 10: Tech debt overview

## Week 3: Independent Work

- First feature ticket assigned
- Code review training
- On-call shadow rotation

## Week 4: Integration

- First on-call shift (with mentor)
- 30-day retro with manager
- Goal-setting for next 60 days"

emit "compliance-attestation" "# SOC 2 Type II Annual Attestation

## Scope

This attestation covers the period January 1, 2025 through December 31, 2025.

## Trust Services Criteria

The following criteria were evaluated:

- Security
- Availability
- Confidentiality

## Findings

No material exceptions identified during the audit period.

## Control Operating Effectiveness

| Control | Status |
|---|---|
| Access reviews quarterly | Effective |
| Encryption in transit | Effective |
| Backup testing | Effective |
| Incident response | Effective |
| Vulnerability management | Effective |

## Auditor

External Auditor LLP, completed Q1 2026."

emit "tutorial-multi-step" "# Tutorial: Build a REST API

## Prerequisites

- Node.js 20+
- Basic JavaScript knowledge

## Step 1: Initialise the project

\`\`\`bash
mkdir my-api && cd my-api
npm init -y
npm install express
\`\`\`

## Step 2: Create the entry point

\`\`\`javascript
const express = require('express');
const app = express();
app.use(express.json());
app.get('/', (req, res) => res.json({ hello: 'world' }));
app.listen(3000);
\`\`\`

## Step 3: Run

\`\`\`bash
node index.js
\`\`\`

## Step 4: Test

\`\`\`bash
curl http://localhost:3000/
\`\`\`

## Step 5: Add a route

Add a POST endpoint that echoes the request body."

emit "questionnaire" "# Customer Discovery Questionnaire

## About your company

1. What is your company's size?
   - Small (under 50)
   - Medium (50-500)
   - Large (500-5000)
   - Enterprise (5000+)

2. What industry are you in?

3. How long has your company existed?

## About your current process

4. How do you currently solve this problem?

5. What tools do you use today?

6. What is your monthly spend on this category?

## About your needs

7. What are your top three priorities for the next 12 months?

8. What would success look like for you?

9. What's your timeline for making a decision?"

emit "internal-newsletter" "# Engineering Newsletter — April 2026

## What we shipped

- New deployment pipeline cuts deploy time by 60%
- Auth service migration complete
- 23 bugs resolved in the bug bash

## What we're working on

- Billing module redesign
- Mobile app beta
- Q3 launch readiness

## Spotlight: Alice

Alice led the auth migration, which spanned 6 weeks and 12 services. Congrats!

## Welcome new hires

- Bob (Senior Engineer)
- Carol (DevOps Engineer)
- Dave (Site Reliability)

## Tech talks this month

- April 28: Distributed systems fundamentals (Bob)
- April 30: Performance engineering (Alice)"

emit "performance-review" "# Annual Performance Review

**Employee:** Sample

**Period:** April 2025 — April 2026

**Manager:** Manager Name

## Achievements

- Led the migration to event-driven architecture
- Mentored two junior engineers
- Authored three RFCs adopted by the team
- Shipped four major features ahead of schedule

## Areas for growth

- More cross-team collaboration on architecture decisions
- Improve documentation of design decisions

## Goals for next year

1. Lead a team of 3-5 engineers
2. Drive a top-priority initiative end-to-end
3. Speak at one external conference

## Compensation

Promoted to Senior Staff Engineer. Compensation adjusted accordingly."

emit "research-summary" "# Literature Review: Federated Learning

## Background

Federated learning trains ML models across distributed devices without centralising data.

## Foundational works

- McMahan et al. (2017): introduced FedAvg algorithm
- Konečný et al. (2016): communication-efficient methods

## Recent advances

- Personalisation: models adapted per-client (e.g., FedPer, pFedMe)
- Privacy: combined with differential privacy and secure aggregation
- Robustness: handling adversarial clients

## Open problems

1. Heterogeneous device capabilities
2. Non-IID data distributions
3. Convergence guarantees with adaptive optimisers

## Conclusion

Federated learning has matured significantly but production deployments remain limited."

emit "data-dictionary" "# Data Dictionary: User Events

## Table: user_events

| Column | Type | Description |
|---|---|---|
| event_id | UUID | Unique event identifier |
| user_id | UUID | Reference to users.id |
| event_type | VARCHAR(50) | One of: login, logout, action |
| event_data | JSONB | Event-specific payload |
| created_at | TIMESTAMP | Event time, UTC |
| ip_address | INET | Source IP |
| user_agent | TEXT | Browser/client identifier |

## Indexes

- Primary key on event_id
- Index on (user_id, created_at)
- Index on (event_type, created_at)

## Retention

Events retained for 90 days, then archived to cold storage."

total=$(ls "$OUT"/*.docx 2>/dev/null | wc -l)
echo ""
echo "Total .docx fixtures in $OUT: $total"
