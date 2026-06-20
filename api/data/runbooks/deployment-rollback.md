# Safe Deployment and Rollback

## Deployment Strategy
- Production uses blue-green deployment with App Service deployment slots.
- New builds deploy to the staging slot first and run smoke tests there.
- Promotion to production is a slot swap, which is near-instant and warms instances before routing traffic.

## Pre-Promotion Checklist
1. All CI checks pass, including unit tests, integration tests, and the load test budget.
2. Database migrations are backward compatible (expand-and-contract). The old version must run against the new schema.
3. Feature flags for risky changes are off by default and ramped gradually.

## Rollback Procedure
1. If errors spike after a swap, immediately swap back. The previous version is still warm in the other slot.
2. Confirm the rollback by watching the error rate and p95 latency return to baseline within 2 minutes.
3. If a database migration is implicated, do not roll the schema back blindly. Because migrations are expand-and-contract, the previous app version remains compatible; fix forward on the data layer.
4. Open an incident, capture the failing build number, and freeze further deploys until root cause is understood.

## Post-Incident
- Add a regression test that reproduces the failure.
- Review whether the smoke tests on the staging slot should have caught it.
- Record the timeline in the incident log.

## Prevention
- Never deploy directly to the production slot.
- Keep migrations backward compatible so rollback never requires a schema change.
- Gate promotion on an automated smoke test against the staging slot.
