using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "loja",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    phone_number_id = table.Column<string>(type: "text", nullable: true),
                    fuso = table.Column<string>(type: "text", nullable: false),
                    horario_inicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    horario_fim = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loja", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "especie",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_popular = table.Column<string>(type: "text", nullable: false),
                    nome_cientifico = table.Column<string>(type: "text", nullable: true),
                    tamanho_cm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    preco = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    observacoes = table.Column<string>(type: "text", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_especie", x => x.id);
                    table.ForeignKey(
                        name: "fk_especie_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fornecedor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    telefone = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fornecedor", x => x.id);
                    table.ForeignKey(
                        name: "fk_fornecedor_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tanque",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    litros = table.Column<int>(type: "integer", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tanque", x => x.id);
                    table.ForeignKey(
                        name: "fk_tanque_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usuario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    papel = table.Column<string>(type: "text", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "foto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    especie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_foto", x => x.id);
                    table.ForeignKey(
                        name: "fk_foto_especie_especie_id",
                        column: x => x.especie_id,
                        principalTable: "especie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_foto_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lote",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    especie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fornecedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tanque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantidade_inicial = table.Column<int>(type: "integer", nullable: false),
                    custo_unitario = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    recebido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quarentena_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lote", x => x.id);
                    table.ForeignKey(
                        name: "fk_lote_especie_especie_id",
                        column: x => x.especie_id,
                        principalTable: "especie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lote_fornecedor_fornecedor_id",
                        column: x => x.fornecedor_id,
                        principalTable: "fornecedor",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_lote_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lote_tanque_tanque_id",
                        column: x => x.tanque_id,
                        principalTable: "tanque",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "movimento_estoque",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tanque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observacao = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimento_estoque", x => x.id);
                    table.ForeignKey(
                        name: "fk_movimento_estoque_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimento_estoque_lote_lote_id",
                        column: x => x.lote_id,
                        principalTable: "lote",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_movimento_estoque_tanque_tanque_id",
                        column: x => x.tanque_id,
                        principalTable: "tanque",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_especie_loja_id",
                table: "especie",
                column: "loja_id");

            migrationBuilder.CreateIndex(
                name: "ix_especie_nome_popular",
                table: "especie",
                column: "nome_popular")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_fornecedor_loja_id",
                table: "fornecedor",
                column: "loja_id");

            migrationBuilder.CreateIndex(
                name: "ix_foto_especie_id",
                table: "foto",
                column: "especie_id");

            migrationBuilder.CreateIndex(
                name: "ix_foto_loja_id",
                table: "foto",
                column: "loja_id");

            migrationBuilder.CreateIndex(
                name: "ix_loja_slug",
                table: "loja",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lote_especie_id",
                table: "lote",
                column: "especie_id");

            migrationBuilder.CreateIndex(
                name: "ix_lote_fornecedor_id",
                table: "lote",
                column: "fornecedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_lote_loja_id",
                table: "lote",
                column: "loja_id");

            migrationBuilder.CreateIndex(
                name: "ix_lote_tanque_id",
                table: "lote",
                column: "tanque_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimento_estoque_loja_id",
                table: "movimento_estoque",
                column: "loja_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimento_estoque_lote_id",
                table: "movimento_estoque",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimento_estoque_tanque_id",
                table: "movimento_estoque",
                column: "tanque_id");

            migrationBuilder.CreateIndex(
                name: "ix_tanque_loja_id",
                table: "tanque",
                column: "loja_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_loja_id_email",
                table: "usuario",
                columns: new[] { "loja_id", "email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "foto");

            migrationBuilder.DropTable(
                name: "movimento_estoque");

            migrationBuilder.DropTable(
                name: "usuario");

            migrationBuilder.DropTable(
                name: "lote");

            migrationBuilder.DropTable(
                name: "especie");

            migrationBuilder.DropTable(
                name: "fornecedor");

            migrationBuilder.DropTable(
                name: "tanque");

            migrationBuilder.DropTable(
                name: "loja");
        }
    }
}
