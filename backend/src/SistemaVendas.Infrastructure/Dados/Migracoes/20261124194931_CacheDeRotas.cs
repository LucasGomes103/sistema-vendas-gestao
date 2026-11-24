using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CacheDeRotas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rota_cache",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    origem_lat = table.Column<double>(type: "double precision", nullable: false),
                    origem_lon = table.Column<double>(type: "double precision", nullable: false),
                    destino_lat = table.Column<double>(type: "double precision", nullable: false),
                    destino_lon = table.Column<double>(type: "double precision", nullable: false),
                    km = table.Column<double>(type: "double precision", nullable: false),
                    calculada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rota_cache", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rota_cache_origem_lat_origem_lon_destino_lat_destino_lon",
                table: "rota_cache",
                columns: new[] { "origem_lat", "origem_lon", "destino_lat", "destino_lon" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rota_cache");
        }
    }
}
