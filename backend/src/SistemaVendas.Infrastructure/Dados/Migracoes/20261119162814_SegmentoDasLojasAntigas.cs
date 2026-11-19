using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaVendas.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class SegmentoDasLojasAntigas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lojas de antes do cadastro público (ECS Fish e a demo) são de aquarismo: seguem falando de espécie e tanque.
            // As cadastradas pelo site já têm o tipo escolhido.
            migrationBuilder.Sql("UPDATE loja SET segmento = 'Aquarismo' WHERE segmento IS NULL OR btrim(segmento) = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
