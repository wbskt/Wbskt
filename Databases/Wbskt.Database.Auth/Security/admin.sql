-- What the platform admin host may read: views over this database that leave out password hashes,
-- token hashes and secrets. Its login is granted SELECT on this schema and nothing else; the views
-- are owned by dbo, so ownership chaining lets them read the dbo tables without a grant on those.
-- A rename here breaks the admin host (wbskt/Wbskt.Admin): AdminViewContractTests pins every column.
CREATE SCHEMA [admin] AUTHORIZATION [dbo];
GO
