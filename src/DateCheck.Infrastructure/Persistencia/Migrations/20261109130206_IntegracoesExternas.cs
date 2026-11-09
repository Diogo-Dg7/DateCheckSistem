using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DateCheck.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class IntegracoesExternas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DiaInteiro",
                table: "Agendamentos",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IdExterno",
                table: "Agendamentos",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Origem",
                table: "Agendamentos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UrlExterna",
                table: "Agendamentos",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agendamentos_Origem_IdExterno",
                table: "Agendamentos",
                columns: new[] { "Origem", "IdExterno" },
                unique: true,
                filter: "[IdExterno] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Agendamentos_Origem_IdExterno",
                table: "Agendamentos");

            migrationBuilder.DropColumn(
                name: "DiaInteiro",
                table: "Agendamentos");

            migrationBuilder.DropColumn(
                name: "IdExterno",
                table: "Agendamentos");

            migrationBuilder.DropColumn(
                name: "Origem",
                table: "Agendamentos");

            migrationBuilder.DropColumn(
                name: "UrlExterna",
                table: "Agendamentos");
        }
    }
}
