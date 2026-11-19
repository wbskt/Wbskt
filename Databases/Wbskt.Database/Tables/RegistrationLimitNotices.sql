-- When a registration through a policy was last announced as refused at a limit, by kind: 'plan' (the
-- account's client limit, PlanClientLimitReachedEvent) or 'policy' (the policy's own, PolicyRegistrationLimitReachedEvent).
-- A board refused at a limit retries every half minute or so; without this each retry wrote a Warning to
-- the audit log (#1081). dbo.RegistrationLimitNotice_Claim lets one through per policy, kind and interval.
CREATE TABLE dbo.RegistrationLimitNotices
(
    PolicyId       INT          NOT NULL,
    Kind           NVARCHAR(16) NOT NULL,
    LastNotifiedAt DATETIME2(3) NOT NULL,
    CreatedAt      DATETIME2(3) NOT NULL CONSTRAINT DF_RegistrationLimitNotices_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_RegistrationLimitNotices PRIMARY KEY (PolicyId, Kind),
    CONSTRAINT FK_RegistrationLimitNotices_Policy FOREIGN KEY (PolicyId) REFERENCES dbo.RegistrationPolicies (Id) ON DELETE CASCADE
);
GO
