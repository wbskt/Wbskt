-- When each payment provider's reconcile (BillingReconcileWorker) last started, so it runs once a day
-- across every auth replica and every restart: dbo.BillingReconcile_Claim takes the day's run.
CREATE TABLE dbo.BillingReconciles
(
    Provider      NVARCHAR(40) NOT NULL,
    LastStartedAt DATETIME2(3) NOT NULL,
    CreatedAt     DATETIME2(3) NOT NULL CONSTRAINT DF_BillingReconciles_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BillingReconciles PRIMARY KEY (Provider)
);
GO
