using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaDockDbContext))]
[Migration("20261022140000_AddRecommendationVoteInvalidationTriggers")]
public partial class AddRecommendationVoteInvalidationTriggers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION mark_recommendation_votes_dirty_from_imdb_titles()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF NEW.media_type = 'movie' THEN
                        UPDATE general_recommendation_settings
                        SET requires_vote_recalculation = TRUE
                        WHERE id = 1;
                    END IF;
                ELSIF (OLD.media_type = 'movie' OR NEW.media_type = 'movie')
                    AND ROW(OLD.year, OLD.imdb_votes, OLD.media_type)
                        IS DISTINCT FROM ROW(NEW.year, NEW.imdb_votes, NEW.media_type) THEN
                    UPDATE general_recommendation_settings
                    SET requires_vote_recalculation = TRUE
                    WHERE id = 1;
                END IF;

                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER trg_imdb_titles_recommendation_votes_dirty
            AFTER INSERT OR UPDATE OF year, imdb_votes, media_type ON imdb_titles
            FOR EACH ROW
            EXECUTE FUNCTION mark_recommendation_votes_dirty_from_imdb_titles();

            CREATE OR REPLACE FUNCTION mark_recommendation_votes_dirty_on_period_change()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF NEW.median_period_years IS DISTINCT FROM OLD.median_period_years THEN
                    NEW.requires_vote_recalculation := TRUE;
                END IF;

                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER trg_general_recommendation_settings_period_changed
            BEFORE UPDATE OF median_period_years ON general_recommendation_settings
            FOR EACH ROW
            EXECUTE FUNCTION mark_recommendation_votes_dirty_on_period_change();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS trg_general_recommendation_settings_period_changed
                ON general_recommendation_settings;
            DROP FUNCTION IF EXISTS mark_recommendation_votes_dirty_on_period_change();

            DROP TRIGGER IF EXISTS trg_imdb_titles_recommendation_votes_dirty ON imdb_titles;
            DROP FUNCTION IF EXISTS mark_recommendation_votes_dirty_from_imdb_titles();
            """);
    }
}