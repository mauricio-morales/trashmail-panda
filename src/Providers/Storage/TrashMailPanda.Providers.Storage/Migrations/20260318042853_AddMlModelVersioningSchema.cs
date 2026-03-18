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
                    ModelId              TEXT    PRIMARY KEY,
                    ModelType            TEXT    NOT NULL,
                    Version              INTEGER NOT NULL,
                    TrainingDate         TEXT    NOT NULL,
                    Algorithm            TEXT    NOT NULL,
                    FeatureSchemaVersion INTEGER NOT NULL,
                    TrainingDataCount    INTEGER NOT NULL,
                    Accuracy             REAL    NOT NULL,
                    MacroPrecision       REAL    NOT NULL,
                    MacroRecall          REAL    NOT NULL,
                    MacroF1              REAL    NOT NULL,
                    PerClassMetricsJson  TEXT    NOT NULL,
                    IsActive             INTEGER NOT NULL DEFAULT 0,
                    FilePath             TEXT    NOT NULL,
                    Notes                TEXT
                );
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS idx_ml_models_active_type
                    ON ml_models (ModelType)
                    WHERE IsActive = 1;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS idx_ml_models_type_version
                    ON ml_models (ModelType, Version DESC);
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS training_events (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    EventAt   TEXT    NOT NULL,
                    EventType TEXT    NOT NULL,
                    ModelType TEXT    NOT NULL,
                    ModelId   TEXT,
                    Details   TEXT    NOT NULL
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS idx_training_events_at
                    ON training_events (EventAt DESC);
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
