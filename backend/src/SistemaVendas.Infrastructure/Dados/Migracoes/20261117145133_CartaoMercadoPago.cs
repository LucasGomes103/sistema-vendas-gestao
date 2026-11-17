using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CartaoMercadoPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "link_cartao",
                table: "pedido",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mp_order_id",
                table: "pedido",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mp_pagamento_id",
                table: "pedido",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "link_cartao",
                table: "pedido");

            migrationBuilder.DropColumn(
                name: "mp_order_id",
                table: "pedido");

            migrationBuilder.DropColumn(
                name: "mp_pagamento_id",
                table: "pedido");
        }
    }
}
