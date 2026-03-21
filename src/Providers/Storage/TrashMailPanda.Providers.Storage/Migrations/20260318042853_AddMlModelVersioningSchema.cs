using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddMlModelVersioningSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS ml_models (
                    model_id               TEXT    PRIMARY KEY,
                    model_type             TEXT    NOT NULL,
                    version                INTEGER NOT NULL,
                    training_date          TEXT    NOT NULL,
                    algorithm              TEXT    NOT NULL,
                    feature_schema_version INTEGER NOT NULL,
                    training_data_count    INTEGER NOT NULL,
                    accuracy               REAL    NOT NULL,
                    macro_precision        REAL    NOT NULL,
                    macro_recall           REAL    NOT NULL,
                    macro_f1               REAL    NOT NULL,
                    per_class_metrics_json TEXT    NOT NULL,
                    is_active              INTEGER NOT NULL DEFAULT 0,
                    file_path              TEXT    NOT NULL,
                    notes                  TEXT
                );
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS idx_ml_models_active_type
                    ON ml_models (model_type)
                    WHERE is_active = 1;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS idx_ml_models_type_version
                    ON ml_models (model_type, version DESC);
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS training_events (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_type   TEXT    NOT NULL,
                    model_type   TEXT    NOT NULL,
                    model_id     TEXT,
                    details_json TEXT,
                    occurred_at  TEXT    NOT NULL DEFAULT (datetime('now'))
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS idx_training_events_at
                    ON training_events (occurred_at DESC);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_training_events_at;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS training_events;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_ml_models_type_version;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_ml_models_active_type;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS ml_models;");
        }
    }
}
