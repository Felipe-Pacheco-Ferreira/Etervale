using FluentMigrator;
using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Persistence.Migrations;

// Simplified Chinese joins the catalog languages, so its *_texts and npc_dialog_lines rows can be stored.
[Migration(53)]
public sealed class M053CatalogSimplifiedChinese : Migration
{
    public override void Up() => Execute.Sql(
        $"INSERT INTO languages (code, name) SELECT '{CatalogLanguages.SimplifiedChinese}', 'Simplified Chinese' "
        + $"WHERE NOT EXISTS (SELECT 1 FROM languages WHERE code = '{CatalogLanguages.SimplifiedChinese}')");

    public override void Down() =>
        Delete.FromTable("languages").Row(new { code = CatalogLanguages.SimplifiedChinese });
}
