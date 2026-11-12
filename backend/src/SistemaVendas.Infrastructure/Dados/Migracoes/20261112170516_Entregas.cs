using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Entregas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entrega",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_pedido = table.Column<int>(type: "integer", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    cep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    endereco = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: true),
                    janela = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    entregador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    saiu_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entregue_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entregue_por = table.Column<Guid>(type: "uuid", nullable: true),
                    observacao = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entrega", x => x.id);
                    table.ForeignKey(
                        name: "fk_entrega_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entrega_pedido_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedido",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_entrega_usuario_entregador_id",
                        column: x => x.entregador_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entrega_entregador_id",
                table: "entrega",
                column: "entregador_id");

            migrationBuilder.CreateIndex(
                name: "ix_entrega_loja_id_status_data",
                table: "entrega",
                columns: new[] { "loja_id", "status", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_entrega_pedido_id",
                table: "entrega",
                column: "pedido_id",
                unique: true);

            // Pedidos pagos antes da agenda existir entram como "a agendar".
            migrationBuilder.Sql("""
                INSERT INTO entrega (id, criado_em, loja_id, pedido_id, numero_pedido, cliente_id, conversa_id, tipo, cep, endereco, status, observacao)
                SELECT gen_random_uuid(), COALESCE(p.pago_em, p.criado_em), p.loja_id, p.id, p.numero, p.cliente_id, p.conversa_id,
                       p.tipo_entrega, p.cep, p.endereco, 'AAgendar', p.observacao
                FROM pedido p
                WHERE p.status = 'Pago';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entrega");
        }
    }
}
