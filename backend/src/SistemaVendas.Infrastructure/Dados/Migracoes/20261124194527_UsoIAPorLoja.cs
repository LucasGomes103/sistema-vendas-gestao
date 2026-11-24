using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class UsoIAPorLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "limite_respostas_ia_mes",
                table: "loja",
                type: "integer",
                nullable: true);

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
                    por_regras = table.Column<int>(type: "integer", nullable: false),
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
                name: "ix_uso_ia_loja_id_dia",
                table: "uso_ia",
                columns: new[] { "loja_id", "dia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "uso_ia");

            migrationBuilder.DropColumn(
                name: "limite_respostas_ia_mes",
                table: "loja");
        }
    }
}
