using FluentMigrator;
using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Persistence.Migrations;

// Filipino joins the catalog languages, so its *_texts and npc_dialog_lines rows can be stored.
[Migration(51)]
public sealed class M051CatalogFilipino : Migration
{
    public override void Up() => Execute.Sql(
        $"INSERT INTO languages (code, name) SELECT '{CatalogLanguages.Filipino}', 'Filipino' "
        + $"WHERE NOT EXISTS (SELECT 1 FROM languages WHERE code = '{CatalogLanguages.Filipino}')");

    public override void Down() =>
        Delete.FromTable("languages").Row(new { code = CatalogLanguages.Filipino });
}
