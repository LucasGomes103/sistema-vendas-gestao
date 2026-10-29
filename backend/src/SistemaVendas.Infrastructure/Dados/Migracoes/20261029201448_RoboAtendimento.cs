using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class RoboAtendimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "aguardando_robo",
                table: "conversa",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "chamou_vendedor",
                table: "conversa",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "chamou_vendedor_em",
                table: "conversa",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_vendedor",
                table: "conversa",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "robo_pausado",
                table: "conversa",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aguardando_robo",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "chamou_vendedor",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "chamou_vendedor_em",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "motivo_vendedor",
                table: "conversa");

            migrationBuilder.DropColumn(
                name: "robo_pausado",
                table: "conversa");
        }
    }
}
