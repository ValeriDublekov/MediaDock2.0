# Internet Exposure Phase 2: API Defenses

## Goal

Reduce information disclosure, cross-site state changes, and resource exhaustion on authenticated and anonymous API routes.

## Tasks

- Change unexpected 500 responses to a generic public problem response with a trace identifier. Keep exception details in protected logs; retain intentional, validated 4xx messages. Add a sentinel-message regression test.
- Inventory every state-changing HTTP endpoint, including uploads and background-job operations. Apply one consistent anti-forgery/origin policy to cookie-authenticated mutations. Remove antiforgery opt-outs where possible; where framework metadata must be disabled for form binding, validate the token explicitly in the handler.
- Update the web API client to send the required token on every applicable mutation. Keep authorization independent from CSRF validation.
- Add route-appropriate rate and concurrency limits for login, session/token issuance, registration requests, expensive search/read routes, uploads, and job dispatch. Coordinate app limits with Cloudflare limits and trust client IP data only from configured proxy networks.
- Preserve request-body, row-count, timeout, and response bounds for uploads and long-running work. Confirm abuse responses use a stable 429/413 contract and do not leak implementation details.

## Verification

- `python -B scripts/run_tests.py server-integration --filter "Category=Api"`
- `python -B scripts/run_tests.py web --filter App.test.tsx`
- Add negative tests for missing/invalid anti-forgery token and cross-origin/same-site mutation attempts; add positive tests for legitimate browser mutations.
- Add rate-limit tests for threshold, 429 response, concurrency behavior, and recovery. Confirm forwarded headers cannot spoof the client identity used by limits.
- Add exception-handler tests proving internal exception text is absent while trace IDs and protected logging remain available.

## Exit Criteria

- No cookie-authenticated state change succeeds without the chosen CSRF/origin protection.
- Unexpected errors return generic responses; expected validation and conflict behavior remains unchanged.
- Anonymous and authenticated abuse controls are documented, tested, and aligned with any edge rules. Upload and queue limits remain effective under parallel requests.
- All affected integration and web tests pass.