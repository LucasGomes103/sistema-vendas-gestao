using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class LoginUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usuario_loja_id_email",
                table: "usuario");

            migrationBuilder.AddColumn<string>(
                name: "senha_hash",
                table: "usuario",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_email",
                table: "usuario",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_loja_id",
                table: "usuario",
                column: "loja_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usuario_email",
                table: "usuario");

            migrationBuilder.DropIndex(
                name: "ix_usuario_loja_id",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "senha_hash",
                table: "usuario");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_loja_id_email",
                table: "usuario",
                columns: new[] { "loja_id", "email" },
                unique: true);
        }
    }
}
