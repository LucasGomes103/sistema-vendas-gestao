using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Pedidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pix_chave",
                table: "loja",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_cidade",
                table: "loja",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_nome",
                table: "loja",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pedido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reserva_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    tipo_entrega = table.Column<string>(type: "text", nullable: false),
                    cep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    endereco = table.Column<string>(type: "text", nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    frete = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pix_copia_e_cola = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    pix_enviado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    comprovante_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    forma_pagamento = table.Column<int>(type: "integer", nullable: true),
                    pago_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_cancelamento = table.Column<string>(type: "text", nullable: true),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    observacao = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedido", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedido_cliente_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pedido_conversa_conversa_id",
                        column: x => x.conversa_id,
                        principalTable: "conversa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pedido_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pedido_reserva_reserva_id",
                        column: x => x.reserva_id,
                        principalTable: "reserva",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "item_pedido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    especie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    especie = table.Column<string>(type: "text", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    preco_unitario = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_pedido", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_pedido_especie_especie_id",
                        column: x => x.especie_id,
                        principalTable: "especie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_pedido_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_item_pedido_pedido_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedido",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_pedido_especie_id",
                table: "item_pedido",
                column: "especie_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_pedido_loja_id",
                table: "item_pedido",
                column: "loja_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_pedido_pedido_id",
                table: "item_pedido",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_cliente_id",
                table: "pedido",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_conversa_id",
                table: "pedido",
                column: "conversa_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_loja_id_numero",
                table: "pedido",
                columns: new[] { "loja_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedido_loja_id_status_criado_em",
                table: "pedido",
                columns: new[] { "loja_id", "status", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_pedido_reserva_id",
                table: "pedido",
                column: "reserva_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_pedido");

            migrationBuilder.DropTable(
                name: "pedido");

            migrationBuilder.DropColumn(
                name: "pix_chave",
                table: "loja");

            migrationBuilder.DropColumn(
                name: "pix_cidade",
                table: "loja");

            migrationBuilder.DropColumn(
                name: "pix_nome",
                table: "loja");
        }
    }
}
