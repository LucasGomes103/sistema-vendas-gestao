using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Atendimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cliente",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: true),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cep = table.Column<string>(type: "text", nullable: true),
                    endereco = table.Column<string>(type: "text", nullable: true),
                    aceita_marketing = table.Column<bool>(type: "boolean", nullable: false),
                    aceita_marketing_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    anonimizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cliente", x => x.id);
                    table.ForeignKey(
                        name: "fk_cliente_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_recebido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    origem = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    processado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    erro = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_recebido", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversa",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ultima_msg_cliente_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultima_mensagem_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aberta = table.Column<bool>(type: "boolean", nullable: false),
                    nao_lidas = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversa", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversa_cliente_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversa_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mensagem",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wamid = table.Column<string>(type: "text", nullable: true),
                    direcao = table.Column<string>(type: "text", nullable: false),
                    autor = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    template = table.Column<string>(type: "text", nullable: true),
                    categoria = table.Column<string>(type: "text", nullable: false),
                    conteudo = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    custo_estimado = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    erro = table.Column<string>(type: "text", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mensagem", x => x.id);
                    table.ForeignKey(
                        name: "fk_mensagem_conversa_conversa_id",
                        column: x => x.conversa_id,
                        principalTable: "conversa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mensagem_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cliente_loja_id_telefone",
                table: "cliente",
                columns: new[] { "loja_id", "telefone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversa_cliente_id",
                table: "conversa",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversa_loja_id_ultima_mensagem_em",
                table: "conversa",
                columns: new[] { "loja_id", "ultima_mensagem_em" });

            migrationBuilder.CreateIndex(
                name: "ix_mensagem_conversa_id_criado_em",
                table: "mensagem",
                columns: new[] { "conversa_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_mensagem_loja_id",
                table: "mensagem",
                column: "loja_id");

            migrationBuilder.CreateIndex(
                name: "ix_mensagem_proxima_tentativa_em",
                table: "mensagem",
                column: "proxima_tentativa_em",
                filter: "status = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_mensagem_wamid",
                table: "mensagem",
                column: "wamid",
                unique: true,
                filter: "wamid IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_recebido_criado_em",
                table: "webhook_recebido",
                column: "criado_em",
                filter: "processado_em IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensagem");

            migrationBuilder.DropTable(
                name: "webhook_recebido");

            migrationBuilder.DropTable(
                name: "conversa");

            migrationBuilder.DropTable(
                name: "cliente");
        }
    }
}
