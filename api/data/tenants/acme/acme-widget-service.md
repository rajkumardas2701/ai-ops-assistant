# ACME Widget Service Runbook

The ACME Widget Service is the catalog API that powers widget browsing and checkout for
ACME-tenant customers. It is owned by the ACME platform team and is isolated from all other
tenants. This runbook is specific to the `acme` tenant and must not appear in any other
tenant's retrieval results.

## Restarting the widget service

1. Confirm the alert is real by checking the `acme-widget` dashboard for elevated 5xx rates.
2. Drain traffic by scaling the `widget-api` deployment connection pool down to zero.
3. Roll the pods: `kubectl rollout restart deploy/widget-api -n acme`.
4. Wait for readiness probes to pass, then restore the connection pool.
5. Verify the widget catalog loads end to end from the ACME storefront.

## Widget catalog cache invalidation

The widget catalog is cached for 15 minutes. After a bulk price update, force a refresh by
publishing a `widget.catalog.invalidate` event to the ACME event bus. Never flush the shared
cache directly — that affects every ACME region at once.

## Common ACME widget alerts

- **WidgetCatalogStale** — the catalog cache has not refreshed in 30 minutes. Replay the last
  `widget.catalog.invalidate` event.
- **WidgetCheckoutLatencyHigh** — p95 checkout latency above 800 ms. Check the ACME payments
  sidecar and the widget inventory lookup.
- **WidgetInventoryDrift** — inventory counts disagree between the widget service and the ACME
  warehouse feed. Trigger a reconciliation job.
