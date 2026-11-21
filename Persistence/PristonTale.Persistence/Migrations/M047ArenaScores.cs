using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The arena (ArenaRules): each week's points, wins and losses per level band and character (the week is its Monday as
// yyyymmdd), and the week's prizes mailed per band and place, which also mark the week as paid.
[Migration(47)]
public sealed class M047ArenaScores : Migration
{
    public override void Up()
    {
        Create.Table("arena_scores")
            .WithColumn("week").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("band").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("character_id").AsInt64().NotNullable().PrimaryKey().ForeignKey("characters", "id")
            .WithColumn("points").AsInt32().NotNullable()
            .WithColumn("wins").AsInt32().NotNullable()
            .WithColumn("losses").AsInt32().NotNullable();
        Create.Table("arena_prizes")
            .WithColumn("week").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("band").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("place").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("character_id").AsInt64().NotNullable()
            .WithColumn("gold").AsInt64().NotNullable()
            .WithColumn("mail_id").AsInt64().Nullable();
    }

    public override void Down()
    {
        Delete.Table("arena_prizes");
        Delete.Table("arena_scores");
    }
}
