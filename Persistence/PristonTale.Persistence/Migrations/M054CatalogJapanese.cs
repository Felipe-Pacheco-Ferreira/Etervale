using FluentMigrator;
using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Persistence.Migrations;

// Japanese joins the catalog languages, so its *_texts and npc_dialog_lines rows can be stored.
[Migration(54)]
public sealed class M054CatalogJapanese : Migration
{
    public override void Up() => Execute.Sql(
        $"INSERT INTO languages (code, name) SELECT '{CatalogLanguages.Japanese}', 'Japanese' "
        + $"WHERE NOT EXISTS (SELECT 1 FROM languages WHERE code = '{CatalogLanguages.Japanese}')");

    public override void Down() =>
        Delete.FromTable("languages").Row(new { code = CatalogLanguages.Japanese });
}
