#!/bin/bash
# Setup Azure Monitor alerts for ArborKin App Service
# Prerequisites: Azure CLI installed and authenticated (`az login`)
# Usage: ./setup-azure-monitoring.sh <resource-group> <app-service-name> <email>

set -e

# Configuration
RESOURCE_GROUP="${1:-arborkin-rg}"
APP_SERVICE="${2:-arborkin-web}"
EMAIL="${3:-doug.rosenberg@gmail.com}"
LOCATION="centralus"

echo "Setting up Azure Monitor alerts for $APP_SERVICE in $RESOURCE_GROUP"
echo "Email alerts will be sent to: $EMAIL"

# 1. Create Action Group (email notification)
ACTION_GROUP_NAME="arborkin-ops-alerts"
ACTION_GROUP_ID=$(az monitor action-group create \
  --resource-group "$RESOURCE_GROUP" \
  --name "$ACTION_GROUP_NAME" \
  --short-name "ArborOps" \
  --query id \
  --output tsv)

echo "✓ Created Action Group: $ACTION_GROUP_NAME"

# 2. Add email receiver to Action Group
az monitor action-group update \
  --resource-group "$RESOURCE_GROUP" \
  --name "$ACTION_GROUP_NAME" \
  --add-action email \
  --action-name "ops-email" \
  --email-receiver "$EMAIL" \
  --output none

echo "✓ Added email receiver: $EMAIL"

# 3. Alert: App Service Availability < 95% for 5 minutes
az monitor metrics alert create \
  --resource-group "$RESOURCE_GROUP" \
  --name "arborkin-availability-alert" \
  --description "Alert when App Service availability drops below 95%" \
  --scopes "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$APP_SERVICE" \
  --condition "avg AvailabilityPercentage < 95" \
  --window-size 5m \
  --evaluation-frequency 1m \
  --action "$ACTION_GROUP_ID" \
  --output none

echo "✓ Created availability alert (< 95% for 5 min)"

# 4. Alert: HTTP 5xx errors spike (> 0 in 1 minute)
az monitor metrics alert create \
  --resource-group "$RESOURCE_GROUP" \
  --name "arborkin-http5xx-alert" \
  --description "Alert when HTTP 5xx errors occur" \
  --scopes "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$APP_SERVICE" \
  --condition "total Http5xx > 0" \
  --window-size 1m \
  --evaluation-frequency 1m \
  --action "$ACTION_GROUP_ID" \
  --output none

echo "✓ Created HTTP 5xx alert (> 0 in 1 min)"

# 5. Alert: Server Response Time > 5 seconds (performance degradation)
az monitor metrics alert create \
  --resource-group "$RESOURCE_GROUP" \
  --name "arborkin-response-time-alert" \
  --description "Alert when average response time exceeds 5 seconds" \
  --scopes "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$APP_SERVICE" \
  --condition "avg ResponseTime > 5000" \
  --window-size 5m \
  --evaluation-frequency 1m \
  --action "$ACTION_GROUP_ID" \
  --output none

echo "✓ Created response time alert (> 5 sec avg for 5 min)"

echo ""
echo "✅ All alerts configured successfully!"
echo ""
echo "Alerts created:"
echo "  • Availability < 95% (5 min window)"
echo "  • HTTP 5xx errors detected (1 min window)"
echo "  • Response time > 5 sec (5 min window)"
echo ""
echo "Notifications will be sent to: $EMAIL"
echo ""
echo "To verify alerts, run:"
echo "  az monitor metrics alert list --resource-group $RESOURCE_GROUP --output table"
