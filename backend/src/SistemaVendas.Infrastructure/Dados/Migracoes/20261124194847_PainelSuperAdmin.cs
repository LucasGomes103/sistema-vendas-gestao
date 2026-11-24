using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class PainelSuperAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ultimo_acesso_em",
                table: "usuario",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "auditoria_admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    acao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    loja_alvo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    loja_alvo_slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    detalhe = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditoria_admin", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "uso_ia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia = table.Column<DateOnly>(type: "date", nullable: false),
                    respostas = table.Column<int>(type: "integer", nullable: false),
                    chamadas = table.Column<int>(type: "integer", nullable: false),
                    tokens_entrada = table.Column<long>(type: "bigint", nullable: false),
                    tokens_saida = table.Column<long>(type: "bigint", nullable: false),
                    falhas = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uso_ia", x => x.id);
                    table.ForeignKey(
                        name: "fk_uso_ia_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_admin_criado_em",
                table: "auditoria_admin",
                column: "criado_em");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_admin_loja_alvo_id",
                table: "auditoria_admin",
                column: "loja_alvo_id");

            migrationBuilder.CreateIndex(
                name: "ix_uso_ia_dia",
                table: "uso_ia",
                column: "dia");

            migrationBuilder.CreateIndex(
                name: "ix_uso_ia_loja_id_dia",
                table: "uso_ia",
                columns: new[] { "loja_id", "dia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria_admin");

            migrationBuilder.DropTable(
                name: "uso_ia");

            migrationBuilder.DropColumn(
                name: "ultimo_acesso_em",
                table: "usuario");
        }
    }
}
