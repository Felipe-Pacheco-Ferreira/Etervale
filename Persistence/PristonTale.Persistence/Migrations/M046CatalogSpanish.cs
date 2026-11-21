using FluentMigrator;
using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Persistence.Migrations;

// Spanish joins the catalog languages, so its *_texts and npc_dialog_lines rows can be stored.
[Migration(46)]
public sealed class M046CatalogSpanish : Migration
{
    public override void Up() => Execute.Sql(
        $"INSERT INTO languages (code, name) SELECT '{CatalogLanguages.Spanish}', 'Spanish' "
        + $"WHERE NOT EXISTS (SELECT 1 FROM languages WHERE code = '{CatalogLanguages.Spanish}')");

    public override void Down() =>
        Delete.FromTable("languages").Row(new { code = CatalogLanguages.Spanish });
}
