using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Caixa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fechamento_caixa",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    entradas = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    saidas = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    saldo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    dinheiro_esperado = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    dinheiro_contado = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    diferenca = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    observacao = table.Column<string>(type: "text", nullable: true),
                    fechado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fechamento_caixa", x => x.id);
                    table.ForeignKey(
                        name: "fk_fechamento_caixa_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lancamento_caixa",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    categoria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: true),
                    forma = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lancamento_caixa", x => x.id);
                    table.ForeignKey(
                        name: "fk_lancamento_caixa_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lancamento_caixa_pedido_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedido",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fechamento_caixa_loja_id_data",
                table: "fechamento_caixa",
                columns: new[] { "loja_id", "data" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lancamento_caixa_loja_id_data",
                table: "lancamento_caixa",
                columns: new[] { "loja_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_lancamento_caixa_pedido_id",
                table: "lancamento_caixa",
                column: "pedido_id",
                unique: true,
                filter: "pedido_id IS NOT NULL");

            // Pedidos pagos antes do caixa existir entram no dia em que foram pagos.
            migrationBuilder.Sql("""
                INSERT INTO lancamento_caixa (id, criado_em, loja_id, tipo, categoria, valor, descricao, forma, data, pedido_id, usuario_id)
                SELECT gen_random_uuid(), p.pago_em, p.loja_id, 'Entrada', 'Venda', p.total, 'Pedido #' || p.numero,
                       CASE p.forma_pagamento WHEN 1 THEN 'Dinheiro' WHEN 2 THEN 'Cartao' ELSE 'Pix' END, (p.pago_em AT TIME ZONE l.fuso)::date, p.id, p.confirmado_por
                FROM pedido p JOIN loja l ON l.id = p.loja_id
                WHERE p.status = 'Pago' AND p.pago_em IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fechamento_caixa");

            migrationBuilder.DropTable(
                name: "lancamento_caixa");
        }
    }
}
