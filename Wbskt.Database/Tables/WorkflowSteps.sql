CREATE TABLE [dbo].[WorkflowSteps] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [WorkflowId] INT NOT NULL,
    [StepOrder] INT NOT NULL, -- Defines the execution order for linear flows
    [Name] NVARCHAR(100) NOT NULL, -- User-friendly name for the step
    [StepType] VARCHAR(50) NOT NULL, -- "Action" or "Modifier"
    [StepIdentifier] VARCHAR(100) NOT NULL, -- e.g., "action.log", "modifier.if"
    [StepConfiguration] NVARCHAR(MAX) NULL, -- JSON configuration for this specific step
    [OnSuccessStepId] INT NULL, -- For branching: ID of the next step on success/true
    [OnFailureStepId] INT NULL, -- For branching: ID of the next step on failure/false
    [PositionX] INT NULL,
    [PositionY] INT NULL,
    CONSTRAINT [PK_WorkflowSteps] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_WorkflowSteps_Workflows] FOREIGN KEY ([WorkflowId]) 
        REFERENCES [dbo].[Workflows]([Id]) ON DELETE CASCADE
);
