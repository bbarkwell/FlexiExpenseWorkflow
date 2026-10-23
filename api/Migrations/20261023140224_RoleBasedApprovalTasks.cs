using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseWorkflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class RoleBasedApprovalTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ResolvedApproverId",
                table: "ApprovalTasks",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "EffectiveApproverId",
                table: "ApprovalTasks",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "AssignedRoleId",
                table: "ApprovalTasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DecidedById",
                table: "ApprovalTasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_AssignedRoleId",
                table: "ApprovalTasks",
                column: "AssignedRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_DecidedById",
                table: "ApprovalTasks",
                column: "DecidedById");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalTasks_Roles_AssignedRoleId",
                table: "ApprovalTasks",
                column: "AssignedRoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalTasks_Users_DecidedById",
                table: "ApprovalTasks",
                column: "DecidedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalTasks_Roles_AssignedRoleId",
                table: "ApprovalTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalTasks_Users_DecidedById",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_AssignedRoleId",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_DecidedById",
                table: "ApprovalTasks");

            migrationBuilder.DropColumn(
                name: "AssignedRoleId",
                table: "ApprovalTasks");

            migrationBuilder.DropColumn(
                name: "DecidedById",
                table: "ApprovalTasks");

            migrationBuilder.AlterColumn<int>(
                name: "ResolvedApproverId",
                table: "ApprovalTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "EffectiveApproverId",
                table: "ApprovalTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
