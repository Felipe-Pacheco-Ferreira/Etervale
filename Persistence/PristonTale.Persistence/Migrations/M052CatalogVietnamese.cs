using FluentMigrator;
using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Persistence.Migrations;

// Vietnamese joins the catalog languages, so its *_texts and npc_dialog_lines rows can be stored.
[Migration(52)]
public sealed class M052CatalogVietnamese : Migration
{
    public override void Up() => Execute.Sql(
        $"INSERT INTO languages (code, name) SELECT '{CatalogLanguages.Vietnamese}', 'Vietnamese' "
        + $"WHERE NOT EXISTS (SELECT 1 FROM languages WHERE code = '{CatalogLanguages.Vietnamese}')");

    public override void Down() =>
        Delete.FromTable("languages").Row(new { code = CatalogLanguages.Vietnamese });
}
