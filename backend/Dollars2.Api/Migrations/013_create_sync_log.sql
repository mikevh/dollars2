IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '013_create_sync_log')
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

    CREATE INDEX IX_SyncLog_AccountId_SyncedAt ON SyncLog (AccountId, SyncedAt DESC);

    INSERT INTO Migrations (ScriptName) VALUES ('013_create_sync_log');
END
