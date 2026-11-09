using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class FotosNoBanco : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arquivo_foto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    foto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conteudo = table.Column<byte[]>(type: "bytea", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquivo_foto", x => x.id);
                    table.ForeignKey(
                        name: "fk_arquivo_foto_foto_foto_id",
                        column: x => x.foto_id,
                        principalTable: "foto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arquivo_foto_foto_id",
                table: "arquivo_foto",
                column: "foto_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "arquivo_foto");
        }
    }
}
