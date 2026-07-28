using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260728090000_RestoreDocumentTemplateFingerprintUniqueIndex")]
public sealed class RestoreDocumentTemplateFingerprintUniqueIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE `DocumentTemplates`
                DROP INDEX `IX_DocumentTemplates_CustomerId_HeadersFingerprint`,
                ADD UNIQUE INDEX `IX_DocumentTemplates_CustomerId_HeadersFingerprint`
                    (`CustomerId`, `HeadersFingerprint`);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE `DocumentTemplates`
                DROP INDEX `IX_DocumentTemplates_CustomerId_HeadersFingerprint`,
                ADD INDEX `IX_DocumentTemplates_CustomerId_HeadersFingerprint`
                    (`CustomerId`, `HeadersFingerprint`);
            """);
    }
}
