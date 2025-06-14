CREATE TABLE [dbo].[Channels] (
    [Id]                INT              IDENTITY (1, 1) NOT NULL,
    [Name]              VARCHAR (100)    NOT NULL,
    [UserId]            INT              NOT NULL,
    [SubscriptionRef]   UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [Fk_Channels_Users]  FOREIGN KEY             ([UserId])  REFERENCES [dbo].[Users]    ([Id]),
    CONSTRAINT [Pk_Channels]        PRIMARY KEY CLUSTERED   ([Id]   ASC),
    CONSTRAINT [Unq_Channels_SubscriptionRef]   UNIQUE      NONCLUSTERED    ([SubscriptionRef]  ASC),
    CONSTRAINT [Unq_Channels_UserId_Name]       UNIQUE      NONCLUSTERED    ([UserId]           ASC, [Name] ASC)
);
