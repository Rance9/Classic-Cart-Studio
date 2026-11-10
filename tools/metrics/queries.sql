-- Classic Cart Studio optional usage counts: the saved aggregate queries (Workers Analytics Engine SQL API). GPL-3.0-or-later.
-- Run with a token that has Account > Account Analytics > Read, against
--   POST https://api.cloudflare.com/client/v4/accounts/<account id>/analytics_engine/sql   (body: one query as text)
-- Columns: blob1 schema, blob2 event, blob3 release version, blob4 surface, blob5 browser family, blob6 system family, blob7 operation,
-- blob8 reason, blob9 timing bucket; double1 = 1; index1 = surface. Analytics Engine may sample stored points: always weight by
-- _sample_interval (SUM(_sample_interval * double1)), never count rows. Counts come only from people who turned reporting on, and only
-- when a request got through (blockers, offline use and failures are missing). They are not people, installations or retention.
-- Replace ccs_usage with ccs_usage_test for the test collector's counts (test traffic only).

-- 1. Events by day, release and surface (the last 30 days)
SELECT toStartOfDay(timestamp) AS day, blob3 AS version, blob4 AS surface, blob2 AS event, SUM(_sample_interval * double1) AS count
FROM ccs_usage
WHERE timestamp > NOW() - INTERVAL '30' DAY
GROUP BY day, version, surface, event
ORDER BY day DESC, version, surface, event
FORMAT JSON;

-- 2. HOME and game starts by release and surface (the last 7 days)
SELECT blob3 AS version, blob4 AS surface, blob2 AS event, SUM(_sample_interval * double1) AS count
FROM ccs_usage
WHERE timestamp > NOW() - INTERVAL '7' DAY AND blob2 IN ('studio_opened', 'home_opened', 'play_started')
GROUP BY version, surface, event
ORDER BY version, surface, event
FORMAT JSON;

-- 3. Backups and restores that finished, and their failures by fixed reason (the last 30 days)
SELECT blob3 AS version, blob4 AS surface, blob2 AS event, blob7 AS operation, blob8 AS reason, SUM(_sample_interval * double1) AS count
FROM ccs_usage
WHERE timestamp > NOW() - INTERVAL '30' DAY AND (blob2 IN ('backup_completed', 'restore_completed') OR (blob2 = 'operation_failed' AND blob7 IN ('backup', 'restore')))
GROUP BY version, surface, event, operation, reason
ORDER BY version, surface, event, operation, reason
FORMAT JSON;

-- 4. All failures by operation and reason (the last 30 days)
SELECT blob3 AS version, blob4 AS surface, blob7 AS operation, blob8 AS reason, SUM(_sample_interval * double1) AS count
FROM ccs_usage
WHERE timestamp > NOW() - INTERVAL '30' DAY AND blob2 = 'operation_failed'
GROUP BY version, surface, operation, reason
ORDER BY count DESC
FORMAT JSON;

-- 5. Browser and system families of the surfaces (the last 30 days)
SELECT blob4 AS surface, blob5 AS browser, blob6 AS system, SUM(_sample_interval * double1) AS count
FROM ccs_usage
WHERE timestamp > NOW() - INTERVAL '30' DAY AND blob2 = 'studio_opened'
GROUP BY surface, browser, system
ORDER BY count DESC
FORMAT JSON;

-- 6. Download clicks on the website (not downloads, not installations: GitHub counts finished asset downloads separately)
SELECT toStartOfDay(timestamp) AS day, SUM(_sample_interval * double1) AS count
FROM ccs_usage
WHERE timestamp > NOW() - INTERVAL '30' DAY AND blob2 = 'download_clicked'
GROUP BY day
ORDER BY day DESC
FORMAT JSON;
