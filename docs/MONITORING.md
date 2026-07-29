# Azure Monitoring & Alerts

ArborKin uses Azure Monitor to track app health and alert on critical issues.

## Alerts Configured

| Alert | Condition | Window | Action |
|-------|-----------|--------|--------|
| **Availability** | < 95% | 5 min | Email ops |
| **HTTP 5xx Errors** | > 0 | 1 min | Email ops |
| **Response Time** | > 5 sec avg | 5 min | Email ops |

All alerts send email notifications to the configured ops address (default: doug.rosenberg@gmail.com).

## Setup

### Initial Setup (One-Time)

```bash
./scripts/setup-azure-monitoring.sh <resource-group> <app-service-name> <email>
```

Example (using defaults):
```bash
./scripts/setup-azure-monitoring.sh
# Uses: arborkin-rg, arborkin-web, doug.rosenberg@gmail.com
```

### Verify Alerts

List all configured alerts:
```bash
az monitor metrics alert list --resource-group arborkin-rg --output table
```

### Update Email Address

To change the ops email:
```bash
az monitor action-group update \
  --resource-group arborkin-rg \
  --name arborkin-ops-alerts \
  --add-action email \
  --action-name "ops-email" \
  --email-receiver "newemail@example.com"
```

## Alert Thresholds

Thresholds are intentionally conservative to minimize false positives:

- **Availability < 95%** — Allows for brief network hiccups; alerts on sustained outage (5 min window)
- **HTTP 5xx > 0** — Any 5xx error triggers immediately (1 min window); catches bugs fast
- **Response time > 5 sec** — Alerts on significant slowness; normal requests take < 1 sec

## Complement to App Code

In addition to Azure Monitor:
- **Migration failures** — Logged and emailed via `Program.cs` try/catch + `IEmailSender`
- **Audit trail** — All user actions logged to database (`AuditLog` table)
- **Error logging** — Serilog to Application Insights (if configured)

## Cost

**Free.** Email notifications are included in App Service pricing. No additional charges for metric alerts or the action group.

## Next: Automated Response

Future: Wire alerts to Azure Automation runbooks for:
- Auto-restart app if availability drops
- Escalate to PagerDuty on critical errors
- Create GitHub issues for 5xx spikes

---

**Reference:** `scripts/setup-azure-monitoring.sh`
