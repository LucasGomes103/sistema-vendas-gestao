using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class AtributosDoProduto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "atributos",
                table: "especie",
                type: "jsonb",
                nullable: true);

            // Produtos que já existem começam sem campos extras (lista vazia, não nula).
            migrationBuilder.Sql("UPDATE especie SET atributos = '[]'::jsonb WHERE atributos IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "atributos",
                table: "especie");
        }
    }
}
