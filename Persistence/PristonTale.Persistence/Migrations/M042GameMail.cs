using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

[Migration(42)]
public sealed class M042GameMail : Migration
{
    public override void Up()
    {
        Create.Table("game_mail")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("sender_account_id").AsInt64().NotNullable().ForeignKey("accounts", "id")
            .WithColumn("recipient_account_id").AsInt64().Nullable().ForeignKey("accounts", "id")
            .WithColumn("subject").AsString(120).NotNullable()
            .WithColumn("body").AsString(8000).NotNullable()
            .WithColumn("item_key").AsString(64).Nullable()
            .WithColumn("item_count").AsInt32().NotNullable()
            .WithColumn("gold").AsInt64().NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable();

        Create.Table("game_mail_receipts")
            .WithColumn("mail_id").AsInt64().NotNullable().ForeignKey("game_mail", "id")
            .WithColumn("account_id").AsInt64().NotNullable().ForeignKey("accounts", "id")
            .WithColumn("read_at").AsDateTime().Nullable()
            .WithColumn("claimed_at").AsDateTime().Nullable();

        Create.Index("ux_game_mail_receipts").OnTable("game_mail_receipts").OnColumn("mail_id").Ascending().OnColumn("account_id").Ascending().WithOptions().Unique();
        Create.Index("ix_game_mail_recipient").OnTable("game_mail").OnColumn("recipient_account_id").Ascending();
    }

    public override void Down()
    {
        Delete.Table("game_mail_receipts");
        Delete.Table("game_mail");
    }
}
