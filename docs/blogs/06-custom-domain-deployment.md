# From Azure URL to Custom Domain: A Practical Deployment Story

**Posted:** July 29, 2026  
**Category:** DevOps & Infrastructure

## The Problem: Generic Azure URLs Aren't Shareable

For three weeks, ArborKin ran at `arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net`. Fine for development, but family members don't want to memorize or share a generated Azure hostname. We needed `arborkin.com` — a real domain.

The challenge wasn't the domain itself (registered at GoDaddy a month prior) but integrating it cleanly with Azure App Service, SSL/TLS, and the running application.

## Step 1: Upgrade App Service Tier

The first blocker: **custom domains require at least Shared tier** on Azure App Service. We were on Free (F1), which has zero support for custom domains or managed SSL certificates.

**Cost tradeoff:** Free ($0) → Shared (~$13/mo), but still cheaper than the original Standard tier estimate.

**Decision:** Upgrade to Basic B1 ($13/mo), which provides:
- Custom domain support ✅
- App Service Managed Certificates (free Let's Encrypt) ✅
- ARR Affinity for Blazor Server SignalR ✅
- 99.95% SLA ✅

No application code changes needed — the tier upgrade happens in Azure Portal and applies immediately.

## Step 2: Add Custom Domain Binding in Azure

In **Azure Portal → App Service → Custom domains**:

1. Click **+ Add custom domain**
2. Enter `arborkin.com`
3. Azure generates a **CNAME record** pointing to the App Service
4. Copy the DNS verification value

The key here is that Azure **does not take over your DNS**. It simply tells you: "Here's the CNAME I want to see," and you add it to your registrar.

## Step 3: Configure DNS at the Registrar (GoDaddy)

At GoDaddy's DNS management:

1. Add a CNAME record:
   - **Name:** `arborkin`
   - **Points to:** `arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net`

2. Save and wait for propagation (15–30 min, sometimes up to 2 hours)

We verified with `nslookup arborkin.com` once propagation completed:
```
Name:    arborkin.com
Address: 13.89.172.3
```

## Step 4: The Surprise: AllowedHosts Configuration

After DNS was live and Azure reported the custom domain as "Secured," accessing `https://arborkin.com` returned:

```
Bad Request - Invalid Hostname
HTTP Error 400. The request hostname is invalid.
```

**Root cause:** ASP.NET Core's host validation middleware checks the `AllowedHosts` setting in `appsettings.json`:

```json
"AllowedHosts": "arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net"
```

The app was *only* allowing requests to the Azure auto-generated hostname, not the custom domain. The fix was a one-line change:

```json
"AllowedHosts": "arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net;arborkin.com;www.arborkin.com"
```

Semicolon-separated list of allowed hostnames. Deploy to Azure, and it works.

## Step 5: Verify HTTPS is Enforced

Azure automatically provisions an SSL certificate for the custom domain via **App Service Managed Certificates**. Verify in Azure Portal:

- **Settings → TLS/SSL settings → HTTPS Only:** On ✅
- **Custom domains:** Shows `arborkin.com` with status "Secured" ✅

## Lessons & Gotchas

1. **Tier constraints are real** — Free tier saves money but blocks entire features (custom domains, managed certs). Know your tier's limits before building on them.

2. **AllowedHosts is a security feature, not just config** — it prevents HOST header injection attacks. It's easy to forget when your DNS works fine but the app still rejects the request.

3. **DNS propagation timing** — browsers cache DNS, so if you tested with the old Azure URL, it might cache the IP. A quick `nslookup` check confirms your registrar's DNS is live before debugging app-layer issues.

4. **Keep both URLs valid during transition** — we kept both `arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net` and `arborkin.com` in AllowedHosts. Old links and bookmarks still work; monitoring/alerts using the Azure URL don't break.

5. **Test domain binding before family launch** — a typo in AllowedHosts (or DNS) reaches users as "Invalid Hostname" with no clear explanation. Worth a 5-minute manual test before announcing the new URL.

## What's Next

- Update Azure Monitor alerts to ping `arborkin.com` instead of the Azure URL
- Redirect `www.arborkin.com` to `arborkin.com` (currently both work, but one canonical URL is cleaner)
- Document the domain setup in the deployment guide for future maintainers

---

**Related:** PR #21 (AllowedHosts update) and Azure App Service Tier Upgrade (2026-07-29)
