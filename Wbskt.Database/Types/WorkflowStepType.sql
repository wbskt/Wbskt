CREATE TYPE [dbo].[WorkflowStepType] AS TABLE(
    [StepOrder] INT NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [StepType] VARCHAR(50) NOT NULL,
    [StepIdentifier] VARCHAR(100) NOT NULL,
    [StepConfiguration] NVARCHAR(MAX) NULL,
    [OnSuccessStepId] INT NULL,
    [OnFailureStepId] INT NULL
);
