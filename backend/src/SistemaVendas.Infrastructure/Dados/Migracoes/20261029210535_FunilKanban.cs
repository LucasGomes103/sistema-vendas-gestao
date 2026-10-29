using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class FunilKanban : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "etapa",
                table: "conversa",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "followups_enviados",
                table: "conversa",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "motivo_perda",
                table: "conversa",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "proxima_acao",
                table: "conversa",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "proxima_acao_em",
                table: "conversa",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "valor_estimado",
                table: "conversa",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "card_evento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    de_etapa = table.Column<int>(type: "integer", nullable: true),
                    para_etapa = table.Column<string>(type: "text", nullable: false),
                    origem = table.Column<string>(type: "text", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detalhe = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_evento", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_evento_conversa_conversa_id",
                        column: x => x.conversa_id,
                        principalTable: "conversa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_card_evento_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "config_funil",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    horas_ate_followup = table.Column<int>(type: "integer", nullable: false),
                    horas_followup1 = table.Column<int>(type: "integer", nullable: false),
                    dias_followup2 = table.Column<int>(type: "integer", nullable: false),
                    horas_ate_perdido = table.Column<int>(type: "integer", nullable: false),
                    texto_followup1 = table.Column<string>(type: "text", nullable: false),
                    texto_followup2 = table.Column<string>(type: "text", nullable: false),
                    texto_frete = table.Column<string>(type: "text", nullable: false),
                    texto_chamar_vendedor = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_config_funil", x => x.id);
                    table.ForeignKey(
                        name: "fk_config_funil_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversa_loja_id_etapa_ultima_mensagem_em",
                table: "conversa",
                columns: new[] { "loja_id", "etapa", "ultima_mensagem_em" });

            migrationBuilder.CreateIndex(
                name: "ix_conversa_proxima_acao_em",
                table: "conversa",
                column: "proxima_acao_em",
                filter: "robo_pausado = false AND proxima_acao_em IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_card_evento_conversa_id",
                table: "card_evento",
                column: "conversa_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_evento_loja_id_criado_em",
                table: "card_evento",
                columns: new[] { "loja_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_config_funil_loja_id",
                table: "config_funil",
                column: "loja_id",
                unique: true);

            // Conversas que já existiam entram no quadro na etapa que corresponde ao estado atual.
            migrationBuilder.Sql("""
                UPDATE conversa SET etapa = 'Quente' WHERE chamou_vendedor;
                UPDATE conversa SET etapa = 'RoboAtendendo'
                WHERE NOT chamou_vendedor
                  AND EXISTS (SELECT 1 FROM mensagem m WHERE m.conversa_id = conversa.id AND m.direcao = 'Saida');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_evento");

            migrationBuilder.DropTable(
                name: "config_funil");

            migrationBuilder.DropIndex(
                name: "ix_conversa_loja_id_etapa_ultima_mensagem_em",
                table: "conversa");

            migrationBuilder.DropIndex(
                name: "ix_conversa_proxima_acao_em",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "etapa",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "followups_enviados",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "motivo_perda",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "proxima_acao",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "proxima_acao_em",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "valor_estimado",
                table: "conversa");
        }
    }
}
