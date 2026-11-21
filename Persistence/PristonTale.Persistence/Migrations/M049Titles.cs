using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Titles (TitleRules): a character's earned and shown titles and its counts (TitleStateCodec), JSON like the other
// owned-state columns ({} for every character stored before), and who holds each position title.
[Migration(49)]
public sealed class M049Titles : Migration
{
    public override void Up()
    {
        Alter.Table("characters").AddColumn("titles").AsString().NotNullable().WithDefaultValue("{}");
        IfDatabase("PostgreSQL").Execute.Sql(
            "ALTER TABLE characters ALTER COLUMN titles DROP DEFAULT, ALTER COLUMN titles TYPE jsonb USING titles::jsonb, " +
            "ALTER COLUMN titles SET DEFAULT '{}'::jsonb;");
        Create.Table("title_holders")
            .WithColumn("title").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("character_id").AsInt64().NotNullable()
            .WithColumn("name").AsString(32).NotNullable()
            .WithColumn("since").AsDateTime().NotNullable();
    }

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down()
    {
        Delete.Table("title_holders");
        Execute.Sql("ALTER TABLE characters DROP COLUMN titles");
    }
}
