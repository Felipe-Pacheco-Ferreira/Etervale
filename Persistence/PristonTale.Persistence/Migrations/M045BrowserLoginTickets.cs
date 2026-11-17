using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

[Migration(45)]
public sealed class M045BrowserLoginTickets : Migration
{
    public override void Up()
    {
        Create.Table("browser_login_tickets")
            .WithColumn("ticket_hash").AsString(64).PrimaryKey()
            .WithColumn("account_id").AsInt64().NotNullable().ForeignKey("accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("password_stamp").AsString(64).NotNullable()
            .WithColumn("expires_unix").AsInt64().NotNullable();
        Create.Index().OnTable("browser_login_tickets").OnColumn("expires_unix").Ascending();
    }

    public override void Down() => Delete.Table("browser_login_tickets");
}
