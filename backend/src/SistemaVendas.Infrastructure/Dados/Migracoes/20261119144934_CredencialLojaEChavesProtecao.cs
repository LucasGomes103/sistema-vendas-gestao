using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CredencialLojaEChavesProtecao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chave_protecao",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chave_protecao", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credencial_loja",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    cifrado = table.Column<string>(type: "text", nullable: false),
                    resumo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credencial_loja", x => x.id);
                    table.ForeignKey(
                        name: "fk_credencial_loja_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credencial_loja_loja_id_tipo",
                table: "credencial_loja",
                columns: new[] { "loja_id", "tipo" },
                unique: true);

            // Segunda barreira no Supabase: a API pública do Supabase (anon/authenticated) não lê estas tabelas.
            // A API conecta como dono das tabelas, que não é afetado pelo RLS.
            migrationBuilder.Sql("ALTER TABLE credencial_loja ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE chave_protecao ENABLE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chave_protecao");

            migrationBuilder.DropTable(
                name: "credencial_loja");
        }
    }
}
