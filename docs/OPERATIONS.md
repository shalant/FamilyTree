# Operations & Deployment Checklist

## Pre-Production: Email Configuration

### Azure App Service Environment Variables

Before going live, set these in **Azure Portal** → **App Service** → **Configuration** → **Application settings**:

| Variable | Value | Purpose |
|----------|-------|---------|
| `Email__FromAddress` | `noreply@arborkin.com` | Story invites from this address |
| `Email__FromName` | `ArborKin` | Display name for emails |
| `Email__SmtpHost` | (your SMTP host) | Email service (optional in dev) |
| `Email__SmtpPort` | `587` | SMTP port |
| `Email__EnableSsl` | `true` | Use TLS encryption |
| `Email__Username` | (SMTP user) | SMTP credentials |
| `Email__Password` | (SMTP password) | Keep in secrets, not config |

**Development fallback:** If `Email__SmtpHost` is not set, emails log to console (no actual sending). This is useful for testing.

### Email Domain Setup

1. **From address must be valid:**
   - If using `noreply@arborkin.com`, the domain must accept mail or have SPF/DKIM records
   - Or use a service like SendGrid/Mailgun that handles this

2. **Optional: Email forwarding for `info@arborkin.com`:**
   - Use Cloudflare Email Routing (free) or your registrar's forwarding
   - Maps `info@arborkin.com` → personal email for receiving inquiries

## Cost Optimization

### Current Setup (as of July 2026)

- **App Service:** Basic B1 tier (~$13/mo)
- **SQL Database:** Serverless (0.5-1 vCore, auto-pause) (~$5-6/mo)
- **Blob Storage:** Minimal usage (~$0.03/mo)
- **Total:** ~$18-20/month for 5 concurrent users

### Cost Drivers to Monitor

1. **SQL Database compute:** Check if workload exceeds 1 vCore consistently
2. **Application Insights:** If enabled, can spike with verbose logging
3. **Data egress:** Outbound bandwidth to external APIs (currently low)
4. **Blob Storage:** Only growing if photos/media explode

### Downgrade Triggers

- If app idles most of the day → reduce to Free F1 App Service tier (but accept 60-min/day compute limit)
- If no errors for a month → disable Application Insights if enabled
- If database consistently idles → reduce min vCores to 0.25

## Monitoring & Alerts

### Error Tracking

**Recommended:** Set up Sentry (free tier: 5k events/month)
- Automatic exception capture
- Real-time alerts on error spikes
- Release tracking

Alternatively: Application Insights (native Azure, ~$2-5/mo)

### Logging

Currently: No persistent logs configured. Options:
1. **Application Insights** — Real-time, queryable
2. **Blob Storage** — Daily `.jsonl` snapshots (cheap, manual queries)
3. **Azure Monitor** — Built-in, limited free tier

### Production Checklist

- [ ] Email config verified in Azure Portal
- [ ] Error tracking tool enabled (Sentry or App Insights)
- [ ] Custom domain (arborkin.com) pointed to Azure deployment
- [ ] SSL certificate auto-renewed (Azure handles this)
- [ ] Backup/disaster recovery plan (Azure SQL backup retention)
- [ ] Database connection string from user secrets/Azure Key Vault
- [ ] Logging level set to Warning (not Debug) to reduce noise

## Deployment

### Manual Deploy (Current)

1. Commit code to `master` branch
2. Go to Azure Portal → App Service → **Deployment Center** → **Redeploy** (or use GitHub Actions)
3. Or: `dotnet publish -c Release` and deploy artifacts

### CI/CD via GitHub Actions

Two workflows active:
- **ci.yml** — Automatic: build + test on every push to `master`
- **deploy-web.yml** — Manual only: requires `workflow_dispatch` trigger

See `.github/workflows/` for details.

### Database Migrations

Migrations run automatically on app startup via `ctx.Database.MigrateAsync()` in `Program.cs`.

If a migration fails:
1. App will NOT start (safe-fail behavior)
2. Email alert sent to `Ops:AlertEmail` (or `SuperUser:Email`)
3. Check migration file in `/src/FamilyTree.Core/Migrations/`
4. Fix and redeploy

## Troubleshooting

### "Invitation required" dead-end pages

**Cause:** User got a story invite link, but token is:
- Expired (default TTL: 30 days, see `Stories:InviteTtlDays`)
- Already used
- Invalid

**Solution:** Sender should create a fresh invite (UI: "Invite" button on person detail).

**Code fix (if pattern recurs):**
- Check `StoryInviteService.ValidateTokenAsync()` return logic
- Ensure error messages are clear and actionable
- Add rate limiting on invite generation if abused

### Story submission loops back to home

**Cause:** After creating an account, user isn't redirected to the family tree.

**Current behavior (fixed):**
1. Submit story → StoryRespond shows success toast
2. Redirect to Register with invite token
3. Create account → success toast
4. Auto-redirect to home or tree (depends on `FocusPersonId`)

If this breaks, check:
- `StoryRespond.razor` redirect logic
- `Register.razor` submit → redirect destination
- Auth claims for `PersonId` / `FocusPersonId`

---

**Last updated:** 2026-07-03  
**Maintained by:** Doug Rosenberg
