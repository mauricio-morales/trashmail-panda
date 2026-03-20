using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrashMailPanda.Providers.Storage.Migrations
{
    /// <summary>
    /// Drops and recreates <c>ml_models</c> and <c>training_events</c> with snake_case column
    /// names to match what <c>ModelVersionRepository</c> expects.
    ///
    /// The original <c>AddMlModelVersioningSchema</c> migration accidentally used PascalCase
    /// column names (e.g. <c>ModelId</c>, <c>ModelType</c>) via raw SQL, whereas the ADO.NET
    /// repository queries them as snake_case (<c>model_id</c>, <c>model_type</c>).  In SQLite
    /// case-insensitivity applies only to letters; underscores are distinct characters, so
    /// <c>ModelId</c> ≠ <c>model_id</c> and every query against the old table raised
    /// "no such column: model_id".
    ///
    /// Because no trained model data can exist yet (the pipeline was just introduced), dropping
    /// and recreating these two tables is safe.
    /// </summary>
    public partial class FixMlTablesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop indexes first (SQLite requires this before dropping tables)
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_ml_models_active_type;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_ml_models_type_version;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_training_events_at;");

            // Drop old tables (PascalCase columns / wrong column names)
            migrationBuilder.Sql("DROP TABLE IF EXISTS ml_models;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS training_events;");

            // Recreate with snake_case column names to match ModelVersionRepository
            migrationBuilder.Sql("""
                CREATE TABLE ml_models (
                    model_id              TEXT    PRIMARY KEY,
                    model_type            TEXT    NOT NULL,
                    version               INTEGER NOT NULL,
                    training_date         TEXT    NOT NULL,
                    algorithm             TEXT    NOT NULL,
                    feature_schema_version INTEGER NOT NULL,
                    training_data_count   INTEGER NOT NULL,
                    accuracy              REAL    NOT NULL,
                    macro_precision       REAL    NOT NULL,
                    macro_recall          REAL    NOT NULL,
                    macro_f1              REAL    NOT NULL,
                    per_class_metrics_json TEXT   NOT NULL,
                    is_active             INTEGER NOT NULL DEFAULT 0,
                    file_path             TEXT    NOT NULL,
                    notes                 TEXT
                );
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX idx_ml_models_active_type
                    ON ml_models (model_type)
                    WHERE is_active = 1;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_ml_models_type_version
                    ON ml_models (model_type, version DESC);
                """);

            migrationBuilder.Sql("""
                CREATE TABLE training_events (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_type   TEXT    NOT NULL,
                    model_type   TEXT    NOT NULL,
                    model_id     TEXT,
                    details_json TEXT,
                    occurred_at  TEXT    NOT NULL DEFAULT (datetime('now'))
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_training_events_at
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
