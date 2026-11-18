using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class VendedorResponsavel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "vendedor_id",
                table: "pedido",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "responsavel_desde",
                table: "conversa",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "responsavel_id",
                table: "conversa",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedido_vendedor_id",
                table: "pedido",
                column: "vendedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversa_responsavel_id",
                table: "conversa",
                column: "responsavel_id",
                filter: "responsavel_id IS NOT NULL");

            // Vendas antigas ficam com quem criou o pedido.
            migrationBuilder.Sql("UPDATE pedido SET vendedor_id = criado_por WHERE vendedor_id IS NULL;");

            // Conversas abertas que já estão com uma pessoa: o responsável é quem respondeu por último.
            migrationBuilder.Sql("""
                UPDATE conversa c
                SET responsavel_id = m.usuario_id, responsavel_desde = m.criado_em
                FROM (
                    SELECT DISTINCT ON (conversa_id) conversa_id, usuario_id, criado_em
                    FROM mensagem
                    WHERE autor = 'Vendedor' AND usuario_id IS NOT NULL
                    ORDER BY conversa_id, criado_em DESC
                ) m
                WHERE m.conversa_id = c.id AND c.aberta AND c.robo_pausado
                  AND EXISTS (SELECT 1 FROM usuario u WHERE u.id = m.usuario_id AND u.ativo);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pedido_vendedor_id",
                table: "pedido");

            migrationBuilder.DropIndex(
                name: "ix_conversa_responsavel_id",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "vendedor_id",
                table: "pedido");

            migrationBuilder.DropColumn(
                name: "responsavel_desde",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "responsavel_id",
                table: "conversa");
        }
    }
}
