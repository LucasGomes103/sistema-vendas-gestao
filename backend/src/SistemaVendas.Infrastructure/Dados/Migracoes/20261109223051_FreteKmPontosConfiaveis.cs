using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class FreteKmPontosConfiaveis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "endereco",
                table: "local_cep",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origem_endereco",
                table: "frete_por_km",
                type: "text",
                nullable: true);

            // Os pontos guardados antes desta correção podem ser o centro da cidade (frete saía 0 km):
            // apaga o cache e faz a loja ser localizada de novo na próxima consulta.
            migrationBuilder.Sql("DELETE FROM local_cep;");
            migrationBuilder.Sql("UPDATE frete_por_km SET origem_lat = 0, origem_lon = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "endereco",
                table: "local_cep");

            migrationBuilder.DropColumn(
                name: "origem_endereco",
                table: "frete_por_km");
        }
    }
}
