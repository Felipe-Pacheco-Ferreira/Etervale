using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The boss roulette (BossRoulette): every draw kept for audit, with its winner, prize, mail and entrants (JSON text).
[Migration(50)]
public sealed class M050BossRoulette : Migration
{
    public override void Up()
    {
        Create.Table("boss_roulette")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("drawn_at").AsDateTime().NotNullable()
            .WithColumn("field_id").AsInt32().NotNullable()
            .WithColumn("boss_code").AsInt32().NotNullable()
            .WithColumn("boss_name").AsString(int.MaxValue).NotNullable()
            .WithColumn("winner_character_id").AsInt64().NotNullable()
            .WithColumn("prize_key").AsString(64).NotNullable()
            .WithColumn("mail_id").AsInt64().Nullable()
            .WithColumn("entrants").AsString(int.MaxValue).NotNullable();
    }

    public override void Down()
    {
        Delete.Table("boss_roulette");
    }
}
