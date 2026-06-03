using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskOne.Migrations
{
    /// <inheritdoc />
    public partial class InitialDocumentDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[UploadedDocuments]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [UploadedDocuments] (
                        [Id] int NOT NULL IDENTITY,
                        [FileName] nvarchar(255) NOT NULL,
                        [FileType] nvarchar(50) NOT NULL,
                        [FilePath] nvarchar(500) NOT NULL,
                        [UploadDate] datetime2 NOT NULL,
                        [Status] nvarchar(50) NOT NULL,
                        [ErrorMessage] nvarchar(max) NULL,
                        [ProcessingTimeMs] int NULL,
                        [RawText] nvarchar(max) NULL,
                        CONSTRAINT [PK_UploadedDocuments] PRIMARY KEY ([Id])
                    );
                END

                IF OBJECT_ID(N'[DocumentMetadata]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [DocumentMetadata] (
                        [Id] int NOT NULL IDENTITY,
                        [DocumentId] int NOT NULL,
                        [PoNumber] nvarchar(100) NULL,
                        [VendorDetails] nvarchar(max) NULL,
                        [PoDate] datetime2 NULL,
                        [DeliveryDate] datetime2 NULL,
                        [DeliverTo] nvarchar(max) NULL,
                        CONSTRAINT [PK_DocumentMetadata] PRIMARY KEY ([Id])
                    );
                END

                IF COL_LENGTH(N'[LineItems]', N'MetadataId') IS NULL AND OBJECT_ID(N'[LineItems]', N'U') IS NOT NULL
                BEGIN
                    ALTER TABLE [LineItems] ADD [MetadataId] int NULL;
                END

                IF OBJECT_ID(N'[LineItems]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [LineItems] (
                        [Id] int NOT NULL IDENTITY,
                        [DocumentId] int NOT NULL,
                        [MetadataId] int NULL,
                        [PoNumber] nvarchar(100) NULL,
                        [Item] nvarchar(max) NULL,
                        [Quantity] decimal(18,4) NULL,
                        [Rate] decimal(18,4) NULL,
                        [TaxPercent] decimal(18,4) NULL,
                        [TaxAmount] decimal(18,4) NULL,
                        [Amount] decimal(18,4) NULL,
                        CONSTRAINT [PK_LineItems] PRIMARY KEY ([Id])
                    );
                END

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_DocumentMetadata_DocumentId' AND [object_id] = OBJECT_ID(N'[DocumentMetadata]'))
                BEGIN
                    CREATE UNIQUE INDEX [IX_DocumentMetadata_DocumentId] ON [DocumentMetadata] ([DocumentId]);
                END

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_LineItems_DocumentId' AND [object_id] = OBJECT_ID(N'[LineItems]'))
                BEGIN
                    CREATE INDEX [IX_LineItems_DocumentId] ON [LineItems] ([DocumentId]);
                END

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_LineItems_MetadataId' AND [object_id] = OBJECT_ID(N'[LineItems]'))
                BEGIN
                    CREATE INDEX [IX_LineItems_MetadataId] ON [LineItems] ([MetadataId]);
                END

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.foreign_key_columns fkc
                    JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
                    WHERE fkc.parent_object_id = OBJECT_ID(N'[DocumentMetadata]')
                      AND fkc.referenced_object_id = OBJECT_ID(N'[UploadedDocuments]')
                      AND pc.[name] = N'DocumentId'
                )
                BEGIN
                    ALTER TABLE [DocumentMetadata] ADD CONSTRAINT [FK_DocumentMetadata_UploadedDocuments_DocumentId]
                    FOREIGN KEY ([DocumentId]) REFERENCES [UploadedDocuments] ([Id]) ON DELETE CASCADE;
                END

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.foreign_key_columns fkc
                    JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
                    WHERE fkc.parent_object_id = OBJECT_ID(N'[LineItems]')
                      AND fkc.referenced_object_id = OBJECT_ID(N'[UploadedDocuments]')
                      AND pc.[name] = N'DocumentId'
                )
                BEGIN
                    ALTER TABLE [LineItems] ADD CONSTRAINT [FK_LineItems_UploadedDocuments_DocumentId]
                    FOREIGN KEY ([DocumentId]) REFERENCES [UploadedDocuments] ([Id]) ON DELETE CASCADE;
                END

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.foreign_key_columns fkc
                    JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
                    WHERE fkc.parent_object_id = OBJECT_ID(N'[LineItems]')
                      AND fkc.referenced_object_id = OBJECT_ID(N'[DocumentMetadata]')
                      AND pc.[name] = N'MetadataId'
                )
                BEGIN
                    ALTER TABLE [LineItems] ADD CONSTRAINT [FK_LineItems_DocumentMetadata_MetadataId]
                    FOREIGN KEY ([MetadataId]) REFERENCES [DocumentMetadata] ([Id]) ON DELETE NO ACTION;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LineItems");

            migrationBuilder.DropTable(
                name: "DocumentMetadata");

            migrationBuilder.DropTable(
                name: "UploadedDocuments");
        }
    }
}
