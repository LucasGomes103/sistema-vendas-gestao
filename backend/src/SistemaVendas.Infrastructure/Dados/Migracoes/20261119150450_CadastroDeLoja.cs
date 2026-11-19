using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CadastroDeLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_confirmado_em",
                table: "usuario",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "segmento",
                table: "loja",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "loja",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Ativa");

            migrationBuilder.CreateTable(
                name: "token_email",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_email", x => x.id);
                    table.ForeignKey(
                        name: "fk_token_email_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_token_email_hash",
                table: "token_email",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_token_email_usuario_id",
                table: "token_email",
                column: "usuario_id");

            // Quem já existia entrou sem confirmação de e-mail: fica como confirmado.
            migrationBuilder.Sql("UPDATE usuario SET email_confirmado_em = criado_em WHERE email_confirmado_em IS NULL;");
            migrationBuilder.Sql("ALTER TABLE token_email ENABLE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "token_email");

            migrationBuilder.DropColumn(
                name: "email_confirmado_em",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "segmento",
                table: "loja");

            migrationBuilder.DropColumn(
                name: "status",
                table: "loja");
        }
    }
}
