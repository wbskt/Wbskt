-- Puts at least one row in every empty user table of the current database, so a migration that
-- would drop or narrow a column meets real data, and sqlpackage's BlockOnPossibleDataLoss has
-- something to block on. Generic on purpose: the schema it seeds is the previous release's, which
-- this repository no longer describes, so a hand-written seed would rot with every schema change.
--
-- Each column gets a value of its type; a foreign key takes a row from the table it references. Tables
-- are retried pass after pass until no more can be filled, which settles foreign-key order without
-- working it out. A table whose constraints refuse both attempts (full values, then defaults and
-- NULLs) is left empty and listed at the end; the rehearsal warns about it rather than failing.
SET NOCOUNT ON;

DECLARE @tables TABLE (object_id int PRIMARY KEY, name nvarchar(300) NOT NULL, seeded bit NOT NULL, error nvarchar(2048) NULL);

INSERT @tables (object_id, name, seeded)
SELECT t.object_id,
       QUOTENAME(s.name) + N'.' + QUOTENAME(t.name),
       CASE WHEN EXISTS (SELECT 1 FROM sys.partitions p WHERE p.object_id = t.object_id AND p.index_id IN (0, 1) AND p.rows > 0) THEN 1 ELSE 0 END
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0
  AND t.temporal_type <> 1;

DECLARE @pass int = 0, @progress bit = 1, @mode int, @oid int, @tname nvarchar(300),
        @cols nvarchar(max), @vals nvarchar(max), @sql nvarchar(max);

WHILE @progress = 1 AND @pass < 25
BEGIN
    SET @pass += 1;
    SET @progress = 0;

    DECLARE pending CURSOR LOCAL FAST_FORWARD FOR SELECT object_id, name FROM @tables WHERE seeded = 0;
    OPEN pending;
    FETCH NEXT FROM pending INTO @oid, @tname;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @mode = 1;
        WHILE @mode <= 2 AND EXISTS (SELECT 1 FROM @tables WHERE object_id = @oid AND seeded = 0)
        BEGIN
            SELECT @cols = STRING_AGG(CAST(QUOTENAME(c.name) AS nvarchar(max)), N', ') WITHIN GROUP (ORDER BY c.column_id),
                   @vals = STRING_AGG(CAST(v.expr AS nvarchar(max)), N', ') WITHIN GROUP (ORDER BY c.column_id)
            FROM sys.columns c
            JOIN sys.types ty ON ty.user_type_id = c.system_type_id
            OUTER APPLY (
                SELECT TOP 1 N'(SELECT TOP 1 ' + QUOTENAME(rc.name) + N' FROM ' + QUOTENAME(rs.name) + N'.' + QUOTENAME(rt.name) + N')' AS expr
                FROM sys.foreign_key_columns fkc
                JOIN sys.tables rt ON rt.object_id = fkc.referenced_object_id
                JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
                JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
                WHERE fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
            ) fk
            CROSS APPLY (SELECT CASE
                WHEN fk.expr IS NOT NULL THEN fk.expr
                WHEN @mode = 2 AND c.default_object_id <> 0 THEN N'DEFAULT'
                WHEN @mode = 2 AND c.is_nullable = 1 THEN N'NULL'
                WHEN ty.name = N'bit' THEN N'1'
                WHEN ty.name IN (N'tinyint', N'smallint', N'int', N'bigint', N'decimal', N'numeric', N'money', N'smallmoney', N'float', N'real') THEN N'1'
                WHEN ty.name IN (N'char', N'varchar') THEN
                    CASE WHEN @mode = 2 AND (c.max_length = -1 OR c.max_length >= 2) THEN N'''{}'''
                         ELSE N'LEFT(''seed-'' + CONVERT(varchar(36), NEWID()), ' + CAST(CASE WHEN c.max_length = -1 THEN 64 ELSE c.max_length END AS nvarchar(10)) + N')' END
                WHEN ty.name IN (N'nchar', N'nvarchar') THEN
                    CASE WHEN @mode = 2 AND (c.max_length = -1 OR c.max_length >= 4) THEN N'N''{}'''
                         ELSE N'LEFT(N''seed-'' + CONVERT(nvarchar(36), NEWID()), ' + CAST(CASE WHEN c.max_length = -1 THEN 64 ELSE c.max_length / 2 END AS nvarchar(10)) + N')' END
                WHEN ty.name = N'uniqueidentifier' THEN N'NEWID()'
                WHEN ty.name = N'date' THEN N'CAST(SYSUTCDATETIME() AS date)'
                WHEN ty.name = N'time' THEN N'CAST(SYSUTCDATETIME() AS time)'
                WHEN ty.name IN (N'datetime', N'datetime2', N'smalldatetime') THEN N'SYSUTCDATETIME()'
                WHEN ty.name = N'datetimeoffset' THEN N'SYSDATETIMEOFFSET()'
                WHEN ty.name IN (N'binary', N'varbinary') THEN N'CRYPT_GEN_RANDOM(' + CAST(CASE WHEN c.max_length = -1 THEN 16 ELSE c.max_length END AS nvarchar(10)) + N')'
                ELSE N'NULL'
            END AS expr) v
            WHERE c.object_id = @oid
              AND c.is_identity = 0
              AND c.is_computed = 0
              AND ty.name NOT IN (N'timestamp', N'rowversion');

            SET @sql = CASE WHEN @cols IS NULL
                THEN N'INSERT INTO ' + @tname + N' DEFAULT VALUES;'
                ELSE N'INSERT INTO ' + @tname + N' (' + @cols + N') VALUES (' + @vals + N');' END;

            BEGIN TRY
                EXEC sys.sp_executesql @sql;
                UPDATE @tables SET seeded = 1, error = NULL WHERE object_id = @oid;
                SET @progress = 1;
            END TRY
            BEGIN CATCH
                UPDATE @tables SET error = ERROR_MESSAGE() WHERE object_id = @oid;
            END CATCH;

            SET @mode += 1;
        END;

        FETCH NEXT FROM pending INTO @oid, @tname;
    END;
    CLOSE pending;
    DEALLOCATE pending;
END;

SELECT N'UNSEEDED|' + name + N'|' + ISNULL(error, N'') FROM @tables WHERE seeded = 0 ORDER BY name;
