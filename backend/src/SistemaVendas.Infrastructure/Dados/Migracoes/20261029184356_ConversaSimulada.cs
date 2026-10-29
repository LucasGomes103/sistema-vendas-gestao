using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ConversaSimulada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "simulada",
                table: "conversa",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Até aqui só existiam conversas do simulador, que usava "sim-{slug}" como número da loja.
            migrationBuilder.Sql("""
                UPDATE conversa SET simulada = true
                WHERE loja_id IN (SELECT id FROM loja WHERE phone_number_id LIKE 'sim-%');
                UPDATE loja SET phone_number_id = NULL WHERE phone_number_id LIKE 'sim-%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "simulada",
                table: "conversa");
        }
    }
}
