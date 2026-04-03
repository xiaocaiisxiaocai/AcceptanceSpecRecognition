using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyTextProcessingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO `MatchingKnowledgeConfigs`
                    (`EntityAliasesJson`, `UnitAliasesJson`, `UnitFactorsJson`, `FieldAliasesJson`, `ConflictPairsJson`, `UpdatedAt`)
                SELECT
                    CAST(
                        JSON_MERGE_PATCH(
                            JSON_OBJECT(
                                'panasonic', '松下',
                                '松下', '松下',
                                'mitsubishi', '三菱',
                                '三菱', '三菱',
                                'delta', '台达',
                                '台达', '台达',
                                'foxconn', '富士康',
                                '富士康', '富士康'
                            ),
                            COALESCE(
                                (
                                    SELECT JSON_OBJECTAGG(`AliasWord`, `StandardWord`)
                                    FROM (
                                        SELECT DISTINCT
                                            LOWER(TRIM(sw.`Word`)) AS `AliasWord`,
                                            CASE LOWER(TRIM(std.`Word`))
                                                WHEN 'panasonic' THEN '松下'
                                                WHEN '松下' THEN '松下'
                                                WHEN 'mitsubishi' THEN '三菱'
                                                WHEN '三菱' THEN '三菱'
                                                WHEN 'delta' THEN '台达'
                                                WHEN '台达' THEN '台达'
                                                WHEN 'foxconn' THEN '富士康'
                                                WHEN '富士康' THEN '富士康'
                                                ELSE NULL
                                            END AS `StandardWord`
                                        FROM `SynonymWords` sw
                                        INNER JOIN `SynonymWords` std
                                            ON std.`GroupId` = sw.`GroupId`
                                           AND std.`IsStandard` = 1
                                        WHERE sw.`IsStandard` = 0
                                          AND TRIM(sw.`Word`) <> ''
                                    ) entity_aliases
                                    WHERE entity_aliases.`StandardWord` IS NOT NULL
                                ),
                                JSON_OBJECT()
                            )
                        ) AS CHAR
                    ) AS `EntityAliasesJson`,
                    CAST(
                        JSON_MERGE_PATCH(
                            JSON_OBJECT(
                                'cm', 'cm',
                                '厘米', 'cm',
                                'mm', 'mm',
                                '毫米', 'mm',
                                'v', 'v',
                                '伏', 'v',
                                'volt', 'v'
                            ),
                            COALESCE(
                                (
                                    SELECT JSON_OBJECTAGG(`AliasWord`, `StandardWord`)
                                    FROM (
                                        SELECT DISTINCT
                                            LOWER(TRIM(sw.`Word`)) AS `AliasWord`,
                                            CASE LOWER(TRIM(std.`Word`))
                                                WHEN 'cm' THEN 'cm'
                                                WHEN '厘米' THEN 'cm'
                                                WHEN '公分' THEN 'cm'
                                                WHEN 'mm' THEN 'mm'
                                                WHEN '毫米' THEN 'mm'
                                                WHEN '公厘' THEN 'mm'
                                                WHEN 'v' THEN 'v'
                                                WHEN '伏' THEN 'v'
                                                WHEN 'volt' THEN 'v'
                                                ELSE NULL
                                            END AS `StandardWord`
                                        FROM `SynonymWords` sw
                                        INNER JOIN `SynonymWords` std
                                            ON std.`GroupId` = sw.`GroupId`
                                           AND std.`IsStandard` = 1
                                        WHERE sw.`IsStandard` = 0
                                          AND TRIM(sw.`Word`) <> ''
                                    ) unit_aliases
                                    WHERE unit_aliases.`StandardWord` IS NOT NULL
                                ),
                                JSON_OBJECT()
                            )
                        ) AS CHAR
                    ) AS `UnitAliasesJson`,
                    CAST(
                        JSON_OBJECT(
                            'mm', 1,
                            'cm', 10,
                            'v', 1
                        ) AS CHAR
                    ) AS `UnitFactorsJson`,
                    CAST(
                        JSON_MERGE_PATCH(
                            JSON_OBJECT(
                                '宽', '宽度',
                                '宽度', '宽度',
                                'width', '宽度',
                                '电压', '电压',
                                '供电电压', '电压',
                                'voltage', '电压',
                                '长度', '长度',
                                '长', '长度',
                                'length', '长度',
                                '高度', '高度',
                                '高', '高度',
                                'height', '高度'
                            ),
                            COALESCE(
                                (
                                    SELECT JSON_OBJECTAGG(`AliasWord`, `StandardWord`)
                                    FROM (
                                        SELECT DISTINCT
                                            LOWER(TRIM(sw.`Word`)) AS `AliasWord`,
                                            CASE LOWER(TRIM(std.`Word`))
                                                WHEN '宽度' THEN '宽度'
                                                WHEN '宽' THEN '宽度'
                                                WHEN 'width' THEN '宽度'
                                                WHEN '电压' THEN '电压'
                                                WHEN '供电电压' THEN '电压'
                                                WHEN 'voltage' THEN '电压'
                                                WHEN '长度' THEN '长度'
                                                WHEN '长' THEN '长度'
                                                WHEN 'length' THEN '长度'
                                                WHEN '高度' THEN '高度'
                                                WHEN '高' THEN '高度'
                                                WHEN 'height' THEN '高度'
                                                ELSE NULL
                                            END AS `StandardWord`
                                        FROM `SynonymWords` sw
                                        INNER JOIN `SynonymWords` std
                                            ON std.`GroupId` = sw.`GroupId`
                                           AND std.`IsStandard` = 1
                                        WHERE sw.`IsStandard` = 0
                                          AND TRIM(sw.`Word`) <> ''
                                    ) field_aliases
                                    WHERE field_aliases.`StandardWord` IS NOT NULL
                                ),
                                JSON_OBJECT()
                            )
                        ) AS CHAR
                    ) AS `FieldAliasesJson`,
                    CAST(
                        JSON_ARRAY(
                            JSON_OBJECT('Left', '输入', 'Right', '输出'),
                            JSON_OBJECT('Left', '投板', 'Right', '收板'),
                            JSON_OBJECT('Left', '放板', 'Right', '收板'),
                            JSON_OBJECT('Left', 'loading', 'Right', 'unloading'),
                            JSON_OBJECT('Left', 'loader', 'Right', 'unloader')
                        ) AS CHAR
                    ) AS `ConflictPairsJson`,
                    UTC_TIMESTAMP() AS `UpdatedAt`
                FROM `SynonymWords`
                WHERE NOT EXISTS (
                    SELECT 1 FROM `MatchingKnowledgeConfigs`
                )
                LIMIT 1;
                """);

            migrationBuilder.Sql("""
                INSERT INTO `MatchingKnowledgeConfigs`
                    (`EntityAliasesJson`, `UnitAliasesJson`, `UnitFactorsJson`, `FieldAliasesJson`, `ConflictPairsJson`, `UpdatedAt`)
                SELECT
                    '{"panasonic":"松下","松下":"松下","mitsubishi":"三菱","三菱":"三菱","delta":"台达","台达":"台达","foxconn":"富士康","富士康":"富士康"}',
                    '{"cm":"cm","厘米":"cm","mm":"mm","毫米":"mm","v":"v","伏":"v","volt":"v"}',
                    '{"mm":1,"cm":10,"v":1}',
                    '{"宽":"宽度","宽度":"宽度","width":"宽度","电压":"电压","供电电压":"电压","voltage":"电压","长度":"长度","长":"长度","length":"长度","高度":"高度","高":"高度","height":"高度"}',
                    '[{"Left":"输入","Right":"输出"},{"Left":"投板","Right":"收板"},{"Left":"放板","Right":"收板"},{"Left":"loading","Right":"unloading"},{"Left":"loader","Right":"unloader"}]',
                    UTC_TIMESTAMP()
                WHERE NOT EXISTS (
                    SELECT 1 FROM `MatchingKnowledgeConfigs`
                );
                """);

            migrationBuilder.DropTable(
                name: "Keywords");

            migrationBuilder.DropTable(
                name: "SynonymWords");

            migrationBuilder.DropTable(
                name: "TextProcessingConfigs");

            migrationBuilder.DropTable(
                name: "SynonymGroups");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Keywords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Word = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Keywords", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SynonymGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SynonymGroups", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TextProcessingConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ConversionMode = table.Column<int>(type: "int", nullable: false),
                    EnableChineseConversion = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EnableKeywordHighlight = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EnableOkNgConversion = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EnableSynonym = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HighlightColorHex = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NgStandardFormat = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OkStandardFormat = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextProcessingConfigs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SynonymWords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    IsStandard = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Word = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SynonymWords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SynonymWords_SynonymGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "SynonymGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Keywords_Word",
                table: "Keywords",
                column: "Word",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SynonymWords_GroupId",
                table: "SynonymWords",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SynonymWords_Word",
                table: "SynonymWords",
                column: "Word");
        }
    }
}
