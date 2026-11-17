using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

[Migration(43)]
public sealed class M043GameMailItems : Migration
{
    public override void Up()
    {
        Create.Table("game_mail_items")
            .WithColumn("mail_id").AsInt64().NotNullable().ForeignKey("game_mail", "id")
            .WithColumn("item_key").AsString(64).NotNullable()
            .WithColumn("quantity").AsInt32().NotNullable();
        Create.Index("ux_game_mail_items").OnTable("game_mail_items")
            .OnColumn("mail_id").Ascending().OnColumn("item_key").Ascending().WithOptions().Unique();
        Execute.Sql("""
            INSERT INTO game_mail_items (mail_id, item_key, quantity)
            SELECT id, item_key, item_count FROM game_mail WHERE item_key IS NOT NULL AND item_count > 0
            """);
    }

    public override void Down() => Delete.Table("game_mail_items");
}
