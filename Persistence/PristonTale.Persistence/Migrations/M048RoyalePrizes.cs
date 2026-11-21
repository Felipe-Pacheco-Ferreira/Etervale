using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The Battle Royale (RoyaleRules): each event's prizes mailed per place (the event is its server-local yyyymmddhh), which
// also mark the event as paid.
[Migration(48)]
public sealed class M048RoyalePrizes : Migration
{
    public override void Up()
    {
        Create.Table("royale_prizes")
            .WithColumn("event_key").AsInt64().NotNullable().PrimaryKey()
            .WithColumn("place").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("character_id").AsInt64().NotNullable()
            .WithColumn("gold").AsInt64().NotNullable()
            .WithColumn("mail_id").AsInt64().Nullable();
    }

    public override void Down()
    {
        Delete.Table("royale_prizes");
    }
}
