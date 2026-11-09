using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Reservas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reserva",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    encerrada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    encerrada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reserva", x => x.id);
                    table.ForeignKey(
                        name: "fk_reserva_conversa_conversa_id",
                        column: x => x.conversa_id,
                        principalTable: "conversa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reserva_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_reserva",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reserva_id = table.Column<Guid>(type: "uuid", nullable: false),
                    especie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    especie = table.Column<string>(type: "text", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tanque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    preco_unitario = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_reserva", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_reserva_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_item_reserva_lote_lote_id",
                        column: x => x.lote_id,
                        principalTable: "lote",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_reserva_reserva_reserva_id",
                        column: x => x.reserva_id,
                        principalTable: "reserva",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_reserva_loja_id",
                table: "item_reserva",
                column: "loja_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_reserva_lote_id",
                table: "item_reserva",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_reserva_reserva_id",
                table: "item_reserva",
                column: "reserva_id");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_conversa_id_status",
                table: "reserva",
                columns: new[] { "conversa_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reserva_expira_em",
                table: "reserva",
                column: "expira_em",
                filter: "status = 'Ativa'");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_loja_id",
                table: "reserva",
                column: "loja_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_reserva");

            migrationBuilder.DropTable(
                name: "reserva");
        }
    }
}
