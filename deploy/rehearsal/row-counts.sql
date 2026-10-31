-- One line per user table: schema.table|rows. Compared before and after the upgrade.
SET NOCOUNT ON;
SELECT s.name + N'.' + t.name + N'|' + CAST(SUM(p.rows) AS nvarchar(20))
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
GROUP BY s.name, t.name
ORDER BY s.name, t.name;
