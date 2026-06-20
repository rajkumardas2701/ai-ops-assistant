# High CPU on Azure App Service

## Symptoms
- App Service plan CPU sustained above 90% for more than 5 minutes.
- Increased response latency (p95 over 2s) and intermittent 502 responses.
- Application Insights shows a spike in request duration and server exceptions.

## Immediate Triage
1. Open the App Service plan in the portal and check the CPU Percentage metric across all instances.
2. Identify whether one instance is hot or all instances are saturated. If a single instance is hot, restart that instance.
3. Check for a recent deployment. If CPU rose right after a deploy, roll back to the previous slot using swap.
4. Scale out by one instance to relieve pressure while you investigate. Autoscale should do this automatically if configured.

## Root Cause Investigation
- Use the Diagnose and solve problems blade and run the CPU profiler for a 60 second capture.
- Look for tight loops, synchronous blocking calls, or runaway garbage collection.
- Check for a traffic surge in the request count metric. A legitimate surge means scale out; a single client hammering the API means add rate limiting.

## Resolution
- If caused by a bad deploy, swap back to the last known good slot.
- If caused by load, increase the autoscale maximum and lower the scale-out CPU threshold to 70%.
- If caused by a code hot path, ship a fix and add a regression test that asserts the endpoint stays under the latency budget.

## Prevention
- Set an autoscale rule: scale out at 70% CPU, scale in at 30%.
- Add an alert at 85% CPU for 5 minutes routed to the on-call channel.
- Load test new releases before promoting to production.
