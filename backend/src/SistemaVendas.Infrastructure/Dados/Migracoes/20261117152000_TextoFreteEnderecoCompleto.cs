using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <summary>
    /// Só dados: a resposta pronta de frete que ainda está no texto padrão antigo ("me envie o seu CEP")
    /// passa a pedir o endereço completo. Texto que a loja mudou fica como está.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261117152000_TextoFreteEnderecoCompleto")]
    public partial class TextoFreteEnderecoCompleto : Migration
    {
        private const string Antigo = "Para calcular o frete, me envie o seu CEP, por favor. Um atendente já te responde com o valor e o prazo.";
        private const string Novo = "Para calcular o frete, me envie o endereço completo de entrega (rua, número, complemento, bairro e CEP), por favor. Um atendente já te responde com o valor e o prazo.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql($"UPDATE config_funil SET texto_frete = '{Novo}' WHERE texto_frete = '{Antigo}';");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql($"UPDATE config_funil SET texto_frete = '{Antigo}' WHERE texto_frete = '{Novo}';");
    }
}
