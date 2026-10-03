-- One row per branch that has contributed to a join cohort. JoinAggregator_Contribute inserts here in
-- the same transaction as the counter update and ignores a repeat, so a branch whose contribution is
-- replayed (crash recovery re-dispatching it after it contributed) cannot be counted twice and close an
-- 'All' join while a member is still running.
CREATE TABLE dbo.JoinContributions
(
    JoinToken UNIQUEIDENTIFIER NOT NULL,
    BranchId  BIGINT NOT NULL,
    CONSTRAINT PK_JoinContributions PRIMARY KEY (JoinToken, BranchId)
);
GO
