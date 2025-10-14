CREATE TABLE [dbo].[WorkflowExecutions] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [WorkflowId] INT NOT NULL,
    [Status] VARCHAR(20) NOT NULL, -- "Pending", "Running", "Success", "Failed"
    [TriggeredAt] DATETIME2 NOT NULL,
    [CompletedAt] DATETIME2 NULL,
    [InitialContext] NVARCHAR(MAX) NULL, -- JSON of the data that started the workflow
    [ErrorLog] NVARCHAR(MAX) NULL, -- Store any exception messages
    CONSTRAINT [PK_WorkflowExecutions] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_WorkflowExecutions_Workflows] FOREIGN KEY ([WorkflowId]) 
        REFERENCES [dbo].[Workflows]([Id]) ON DELETE CASCADE
);
