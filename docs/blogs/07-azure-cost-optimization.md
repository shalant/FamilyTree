# From $241/mo to $20/mo: Optimizing Azure Costs for a Family Tree App

**Posted:** July 29, 2026  
**Category:** Infrastructure & Cost Optimization

## The Starting Point: Expensive Defaults

When we first deployed ArborKin to Azure in June, the bill looked like this:

| Service | Tier | Monthly Cost |
|---------|------|--------------|
| App Service | Standard S1 | ~$69 |
| Azure SQL Database | Standard | ~$150 |
| Blob Storage | LRS | ~$1 |
| **Total** | | **~$220–$241** |

For a family tree app with 5 users and <1 GB of photos, this was overkill. We were paying for production-grade performance we didn't need.

## The Reality Check

During user testing, we observed:
- Peak traffic: ~3 concurrent users (not simultaneous requests, literally 3 people browsing occasionally)
- Database queries: simple person/relationship lookups, no complex aggregations
- Blob storage: <50 MB of photos
- App CPU/memory: consistently <10% utilization

We were provisioning for thousands of users while serving a family.

## Round 1: Database Optimization

**Original:** Standard tier Azure SQL Database

The Standard tier is designed for production workloads with 24/7 uptime requirements. For a hobby/family project, this is wasteful.

**Options considered:**
1. **Serverless (Basic Compute)** — $5/mo, auto-pauses after 1 hour of inactivity
   - Pro: Cheapest
   - Con: 30–60 second cold start after pause (noticeable for users)

2. **Basic DTU (5 DTU)** — ~$7/mo, always on
   - Pro: Low cost, no cold-start delays
   - Con: Minimal resources (fine for our workload)

**Decision:** Basic DTU tier (~$5–7/mo)

**Implementation:**
- No schema changes — Azure SQL Basic supports the full EF Core feature set
- No connection string changes — same `Server=...database.windows.net` endpoint
- No application code changes
- Migration: Click "Scale" in Azure Portal, select Basic, apply

**Result:** $150/mo → $5–7/mo. Worst case, a complex analytics query takes 50ms longer. Best case, we'll never notice.

**Risk mitigated:** If traffic spikes unexpectedly, we upgrade with a 5-minute portal click.

## Round 2: App Service Tier Reduction

**Original:** Standard S1 tier ($69/mo)

Standard tier provides:
- Auto-scale (not needed — 3 users)
- Multiple deployment slots (not needed — we deploy manually)
- Zone redundancy (not needed — family app, single region is fine)

**Options considered:**
1. **Free (F1)** — $0/mo
   - Pro: Free
   - Con: Spins down after 20 min inactivity (kills Blazor Server WebSocket connections)
   - Con: No custom domains or managed SSL

2. **Shared** — ~$13/mo
   - Pro: Cheap, supports custom domains
   - Con: Still overkill

3. **Basic B1** — ~$13/mo
   - Pro: Cheap, supports custom domains and managed certificates
   - Pro: ARR Affinity (required for Blazor Server)
   - Pro: 99.95% SLA
   - Con: Manual scaling only

**Decision:** Basic B1 (~$13/mo)

**Why not Free?**
- Blazor Server requires **Always On** to keep WebSocket connections alive
- Free tier has no Always On; connections drop frequently
- Free tier has no custom domain support
- The difference ($0 vs $13) is worth the reliability

**Why not Shared?**
- B1 costs the same but provides SLA and better support

**Result:** $69/mo → $13/mo

## Round 3: Storage & Other Services

- **Blob Storage:** Already minimal at $1/mo (LRS redundancy is cheap)
- **Application Insights:** Auto-attached (default), ~$2–5/mo depending on volume — disabled (we have logs via Application Insights without paying extra)

## Final Bill

| Service | Tier | Monthly Cost |
|---------|------|--------------|
| App Service | Basic B1 | ~$13 |
| Azure SQL Database | Basic 5 DTU | ~$5–7 |
| Blob Storage | LRS | ~$1 |
| **Total** | | **~$19–21/mo** |

**From $241 → $20. 92% reduction.**

## The Trade-offs We Made

1. **No auto-scaling** — if 1000 people join tomorrow, the app will slow down. Reality: we'll see 5 users. If it happens, we upgrade the tier in one click.

2. **No deployment slots** — can't blue-green deploy without downtime. Reality: deployments are fast and infrequent, and a few seconds of downtime for a family app is acceptable.

3. **No multi-region redundancy** — single region means a regional Azure outage affects us. Reality: Azure's regional outages are rare, and our backups (automatic snapshots) survive them.

4. **Database cold start on long idle** — if no one uses the app for 1+ hour, the first query after idle takes 30–60s. Reality: not a problem yet, and if it becomes one, we upgrade to always-on.

## Monitoring the Gamble

We kept **Azure Monitor alerts** configured:
- Availability < 95% (5 min window) → email alert
- HTTP 5xx errors → immediate alert
- Response time > 5 sec (5 min window) → email alert

If the Basic tier becomes a constraint, the alerts will tell us. We'll upgrade before users feel it.

## Lessons for Cost Optimization

1. **Start with production-grade, optimize based on observed usage** — we knew our Azure bill was high, but the monitoring data proved where the waste was.

2. **Understand the cost drivers** — database tier and app service tier were 90% of the bill. Everything else was noise.

3. **Know your non-negotiables** — Blazor Server *requires* Always On. That's our tier floor. Everything else is a preference, not a constraint.

4. **Cost reduction has trade-offs** — we traded auto-scaling and multi-region for 92% savings. The math was obvious for a family app.

5. **Automate the monitoring** — if we bet on low resource usage, we need alerts to catch if the bet breaks. Set them up *before* you optimize.

## What Didn't Change

- No database refactoring (schema is already efficient)
- No caching layer added (EF Core's query execution is fast enough)
- No CDN added (static assets are small, Azure CDN would cost more than it saves)
- No Docker optimization (Blazor Server already runs lean)

We optimized the *infrastructure*, not the application.

---

**Related:** Azure App Service Tier Selection (2026-07-29) and Deployment Guide (`docs/deployment.md`)
