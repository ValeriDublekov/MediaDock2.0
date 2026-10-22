-- TEMPLATE ONLY. Copy to .test-tmp/golden-globes-<year>.sql and replace each
-- commented input block with verified values before use. This file is intentionally
-- empty of import data; its preflight raises until all input tables are populated.
-- Default scope is existing golden_globe_awards only. This template creates no
-- award or background_job rows. For a specifically approved new category, add
-- an idempotent golden_globe_awards insert after BEGIN and before the preflight.
-- Never execute an import against Prod.

BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

CREATE TEMP TABLE gg_import_totals (
    ceremony_year integer NOT NULL CHECK (ceremony_year BETWEEN 1800 AND 2200),
    expected_category_count integer NOT NULL CHECK (expected_category_count > 0),
    expected_nomination_count integer NOT NULL CHECK (expected_nomination_count > 0),
    expected_title_group_count integer NOT NULL CHECK (expected_title_group_count > 0)
) ON COMMIT DROP;

-- Replace with exactly one row, using independently checked audit totals:
-- INSERT INTO gg_import_totals VALUES (<year>, <included categories>, <import rows>, <title groups>);

CREATE TEMP TABLE gg_import_categories (
    page_category text PRIMARY KEY,
    award_name text NOT NULL,
    expected_nomination_count integer NOT NULL CHECK (expected_nomination_count > 0),
    expected_winner_count integer NOT NULL CHECK (
        expected_winner_count >= 0 AND expected_winner_count <= expected_nomination_count
    )
) ON COMMIT DROP;

-- Replace with one row per included rendered IMDb category. Winner counts may be
-- zero or greater than one when the source shows no winner or a tie:
-- INSERT INTO gg_import_categories VALUES
--     ('<IMDb page category>', '<exact existing golden_globe_awards.name>', <rows>, <winners>);

CREATE TEMP TABLE gg_import_source_rows (
    page_category text NOT NULL REFERENCES gg_import_categories (page_category),
    year integer NOT NULL CHECK (year BETWEEN 1800 AND 2200),
    winner boolean NOT NULL,
    nominee_type text NOT NULL CHECK (nominee_type IN ('movie', 'series')),
    title text NOT NULL CHECK (btrim(title) <> ''),
    imdb_id varchar(32) NOT NULL CHECK (imdb_id ~ '^tt[0-9]{7,10}$'),
    import_key text NOT NULL CHECK (import_key ~ '^[0-9a-f]{64}$'),
    PRIMARY KEY (import_key)
) ON COMMIT DROP;

-- Replace with one row per unique importer identity. Double apostrophes in SQL
-- string literals; preserve title spelling, Unicode, source category, and IMDb ID.
-- Compute import_key from the exact database award_name, not page_category:
-- lower(hex(SHA256(UTF8('v1:' || year || U+001F || 'True'/'False' || U+001F || award_name || U+001F || title))))
-- INSERT INTO gg_import_source_rows
--     (page_category, year, winner, nominee_type, title, imdb_id, import_key)
-- VALUES ('<IMDb page category>', <year>, <TRUE|FALSE>, '<movie|series>', '<title>', '<tt id>', '<lowercase sha256>');

DO $$
DECLARE
    totals record;
BEGIN
    IF (SELECT count(*) FROM gg_import_totals) <> 1 THEN
        RAISE EXCEPTION 'Populate exactly one expected-totals row before importing.';
    END IF;

    SELECT * INTO totals FROM gg_import_totals;

    IF NOT EXISTS (SELECT 1 FROM gg_import_source_rows) THEN
        RAISE EXCEPTION 'Populate verified source rows before importing.';
    END IF;

    IF (SELECT count(*) FROM gg_import_categories) <> totals.expected_category_count THEN
        RAISE EXCEPTION 'Expected category count does not match the category map.';
    END IF;

    IF (SELECT count(*) FROM gg_import_source_rows) <> totals.expected_nomination_count THEN
        RAISE EXCEPTION 'Expected nomination count does not match the source rows.';
    END IF;

    IF (SELECT count(*) FROM (
        SELECT DISTINCT title, year, nominee_type FROM gg_import_source_rows
    ) AS title_groups) <> totals.expected_title_group_count THEN
        RAISE EXCEPTION 'Expected title-group count does not match the source rows.';
    END IF;

    IF EXISTS (
        SELECT 1 FROM gg_import_source_rows
        WHERE year <> totals.ceremony_year
    ) THEN
        RAISE EXCEPTION 'A source row has a different ceremony year.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM gg_import_categories AS expected
        LEFT JOIN gg_import_source_rows AS source USING (page_category)
        GROUP BY expected.page_category,
                 expected.expected_nomination_count,
                 expected.expected_winner_count
        HAVING count(source.import_key) <> expected.expected_nomination_count
            OR count(source.import_key) FILTER (WHERE source.winner) <> expected.expected_winner_count
    ) THEN
        RAISE EXCEPTION 'A category nomination or winner count differs from the audited source.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM gg_import_categories AS expected
        LEFT JOIN golden_globe_awards AS award ON award.name = expected.award_name
        WHERE award.id IS NULL
    ) THEN
        RAISE EXCEPTION 'A mapped award is not defined in golden_globe_awards.';
    END IF;

    IF EXISTS (
        SELECT title, year, nominee_type
        FROM gg_import_source_rows
        GROUP BY title, year, nominee_type
        HAVING count(DISTINCT imdb_id) <> 1
    ) THEN
        RAISE EXCEPTION 'A title group maps to more than one IMDb ID.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM (
            SELECT DISTINCT title, year, nominee_type, imdb_id
            FROM gg_import_source_rows
        ) AS source
        JOIN golden_globe_titles AS existing
          ON existing.title = source.title
         AND existing.year = source.year
         AND existing.nominee_type = source.nominee_type
        WHERE existing.imdb_id IS DISTINCT FROM source.imdb_id
           OR existing.is_imdb_id_manual IS NOT TRUE
    ) THEN
        RAISE EXCEPTION 'An existing title group has a different or unpinned IMDb ID.';
    END IF;
END
$$;

-- Add pending canonical rows only when absent; do not overwrite enriched metadata.
INSERT INTO imdb_titles (imdb_id, metadata_status, enrichment_attempt_count, updated_at)
SELECT DISTINCT imdb_id, 'pending', 0, CURRENT_TIMESTAMP
FROM gg_import_source_rows
ON CONFLICT (imdb_id) DO NOTHING;

-- Add one pinned source group for each distinct title/year/type identity.
INSERT INTO golden_globe_titles
    (title, year, nominee_type, imdb_id, is_imdb_id_manual, imdb_id_version,
     enrichment_status, enrichment_attempt_count)
SELECT title, year, nominee_type, imdb_id, TRUE, 1, 'pending', 0
FROM (
    SELECT DISTINCT title, year, nominee_type, imdb_id
    FROM gg_import_source_rows
) AS source_groups
WHERE TRUE
ON CONFLICT (title, year, nominee_type) DO NOTHING;

-- Map IMDb labels to existing DB awards; only add nominations, never award names.
INSERT INTO golden_globe_nominations (import_key, winner, award_id, golden_globe_title_id)
SELECT source.import_key, source.winner, award.id, title.id
FROM gg_import_source_rows AS source
JOIN gg_import_categories AS category ON category.page_category = source.page_category
JOIN golden_globe_awards AS award ON award.name = category.award_name
JOIN golden_globe_titles AS title
  ON title.title = source.title
 AND title.year = source.year
 AND title.nominee_type = source.nominee_type
WHERE TRUE
ON CONFLICT (import_key) DO NOTHING;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM gg_import_source_rows AS source
        JOIN gg_import_categories AS category ON category.page_category = source.page_category
        LEFT JOIN golden_globe_awards AS award ON award.name = category.award_name
        LEFT JOIN golden_globe_titles AS title
          ON title.title = source.title
         AND title.year = source.year
         AND title.nominee_type = source.nominee_type
         AND title.imdb_id = source.imdb_id
         AND title.is_imdb_id_manual IS TRUE
        LEFT JOIN golden_globe_nominations AS nomination
          ON nomination.import_key = source.import_key
         AND nomination.award_id = award.id
         AND nomination.winner = source.winner
         AND nomination.golden_globe_title_id = title.id
        WHERE nomination.id IS NULL
    ) THEN
        RAISE EXCEPTION 'Post-import verification failed; transaction must roll back.';
    END IF;
END
$$;

SELECT award.name,
       count(*) AS nominations,
       count(*) FILTER (WHERE nomination.winner) AS winners
FROM gg_import_source_rows AS source
JOIN gg_import_categories AS category ON category.page_category = source.page_category
JOIN golden_globe_awards AS award ON award.name = category.award_name
JOIN golden_globe_nominations AS nomination ON nomination.import_key = source.import_key
GROUP BY award.name
ORDER BY award.name;

COMMIT;