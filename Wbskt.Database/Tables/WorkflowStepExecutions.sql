CREATE TABLE [dbo].[WorkflowStepExecutions] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [WorkflowExecutionId] INT NOT NULL,
    [WorkflowStepId] INT NOT NULL,
    [Status] VARCHAR(20) NOT NULL, -- "Success", "Failed", "Skipped"
    [StartedAt] DATETIME2 NOT NULL,
    [CompletedAt] DATETIME2 NULL,
    [InputContext] NVARCHAR(MAX) NULL, -- The workflow context as it was BEFORE this step ran
    [OutputContext] NVARCHAR(MAX) NULL, -- The workflow context as it was AFTER this step ran
    [ErrorLog] NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_WorkflowStepExecutions] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_StepExecutions_MainExecution] FOREIGN KEY ([WorkflowExecutionId]) REFERENCES [dbo].[WorkflowExecutions]([Id]) ON DELETE CASCADE
);
