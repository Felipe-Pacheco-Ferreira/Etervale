using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// A character's events and rewards (RewardEventStateCodec): JSON, jsonb on PostgreSQL like the other owned-state columns;
// {} for every character stored before, which then starts the welcome campaign on its next entry.
[Migration(41)]
public sealed class M041RewardEvents : Migration
{
    public override void Up()
    {
        Alter.Table("characters").AddColumn("events").AsString().NotNullable().WithDefaultValue("{}");
        IfDatabase("PostgreSQL").Execute.Sql(
            "ALTER TABLE characters ALTER COLUMN events DROP DEFAULT, ALTER COLUMN events TYPE jsonb USING events::jsonb, " +
            "ALTER COLUMN events SET DEFAULT '{}'::jsonb;");
    }

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down()
    {
        Execute.Sql("ALTER TABLE characters DROP COLUMN events");
    }
}
