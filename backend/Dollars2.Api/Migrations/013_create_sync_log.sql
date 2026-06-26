IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SyncLog')
BEGIN
    CREATE TABLE SyncLog (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        AccountId INT NOT NULL,
        SyncedAt DATETIME2 NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        TransactionCount INT NOT NULL DEFAULT 0,
        ErrorMessage NVARCHAR(MAX) NULL,
        CONSTRAINT FK_SyncLog_Accounts FOREIGN KEY (AccountId) REFERENCES Accounts(Id)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncLog_AccountId_SyncedAt')
BEGIN
    CREATE INDEX IX_SyncLog_AccountId_SyncedAt ON SyncLog (AccountId, SyncedAt DESC);
END
