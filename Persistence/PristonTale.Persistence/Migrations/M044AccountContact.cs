using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The e-mail and referral code the website's registration asks for. Accounts made before (or by the account tool) have
// neither; an e-mail, when present, belongs to one account.
[Migration(44)]
public sealed class M044AccountContact : Migration
{
    public override void Up()
    {
        Alter.Table("accounts")
            .AddColumn("email").AsString(254).Nullable()
            .AddColumn("referral_code").AsString(32).Nullable();
        Create.Index("ux_accounts_email").OnTable("accounts").OnColumn("email").Ascending().WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Index("ux_accounts_email").OnTable("accounts");
        Delete.Column("email").FromTable("accounts");
        Delete.Column("referral_code").FromTable("accounts");
    }
}
