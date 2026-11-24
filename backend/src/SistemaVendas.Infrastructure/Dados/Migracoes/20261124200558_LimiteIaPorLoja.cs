using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class LimiteIaPorLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "respostas_ia",
                table: "uso_ia",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "limite_respostas_ia_mes",
                table: "loja",
                type: "integer",
                nullable: true);

            // Uso já gravado: respostas que tiveram chamada à IA, menos as que falharam.
            migrationBuilder.Sql("update uso_ia set respostas_ia = greatest(respostas - falhas, 0) where chamadas > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "respostas_ia",
                table: "uso_ia");

            migrationBuilder.DropColumn(
                name: "limite_respostas_ia_mes",
                table: "loja");
        }
    }
}
