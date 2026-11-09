using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class LeadQuenteSemDono : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "atendido_em",
                table: "conversa",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "avisos_quente",
                table: "conversa",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Quem já tinha resposta de vendedor depois do chamado não está "sem dono".
            migrationBuilder.Sql("""
                UPDATE conversa c SET atendido_em = m.primeira
                FROM (SELECT conversa_id, min(criado_em) AS primeira FROM mensagem WHERE autor = 'Vendedor' GROUP BY conversa_id) m
                WHERE m.conversa_id = c.id AND c.chamou_vendedor AND m.primeira >= c.chamou_vendedor_em;
                """);
            // Chamados antigos não disparam a mensagem de 1 h de uma vez para todo mundo.
            migrationBuilder.Sql("UPDATE conversa SET avisos_quente = 2 WHERE chamou_vendedor AND chamou_vendedor_em < now() - interval '1 hour';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "atendido_em",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "avisos_quente",
                table: "conversa");
        }
    }
}
