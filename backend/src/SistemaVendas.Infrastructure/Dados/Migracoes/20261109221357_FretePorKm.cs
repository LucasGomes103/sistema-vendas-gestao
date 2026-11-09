using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class FretePorKm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "frete_por_km",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    cep_origem = table.Column<int>(type: "integer", nullable: false),
                    origem_lat = table.Column<double>(type: "double precision", nullable: false),
                    origem_lon = table.Column<double>(type: "double precision", nullable: false),
                    km_inclusos = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    valor_base = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    valor_por_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    km_maximo = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    dias = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_frete_por_km", x => x.id);
                    table.ForeignKey(
                        name: "fk_frete_por_km_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "local_cep",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cep = table.Column<int>(type: "integer", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lon = table.Column<double>(type: "double precision", nullable: false),
                    fonte = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_cep", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_frete_por_km_loja_id",
                table: "frete_por_km",
                column: "loja_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_cep_cep",
                table: "local_cep",
                column: "cep",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "frete_por_km");

            migrationBuilder.DropTable(
                name: "local_cep");
        }
    }
}
