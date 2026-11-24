using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class PlanosEAssinatura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assinatura",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plano = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    teste_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pago_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cortesia_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_cortesia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    mp_assinatura_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    mp_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    cobrancas_creditadas = table.Column<int>(type: "integer", nullable: false),
                    email_pagador = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cancelada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assinatura", x => x.id);
                    table.ForeignKey(
                        name: "fk_assinatura_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pagamento_assinatura",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mp_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mp_assinatura_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pagamento_assinatura", x => x.id);
                    table.ForeignKey(
                        name: "fk_pagamento_assinatura_loja_loja_id",
                        column: x => x.loja_id,
                        principalTable: "loja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assinatura_loja_id",
                table: "assinatura",
                column: "loja_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assinatura_mp_assinatura_id",
                table: "assinatura",
                column: "mp_assinatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_pagamento_assinatura_loja_id",
                table: "pagamento_assinatura",
                column: "loja_id");

            migrationBuilder.CreateIndex(
                name: "ix_pagamento_assinatura_mp_id",
                table: "pagamento_assinatura",
                column: "mp_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assinatura");

            migrationBuilder.DropTable(
                name: "pagamento_assinatura");
        }
    }
}
