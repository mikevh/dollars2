IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountBalances')
BEGIN
    CREATE TABLE AccountBalances (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        AccountId INT NOT NULL,
        Balance DECIMAL(18,2) NOT NULL,
        CreatedOn DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedOn DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_AccountBalances_Accounts FOREIGN KEY (AccountId) REFERENCES Accounts(Id)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccountBalances_AccountId_CreatedOn')
BEGIN
    CREATE INDEX IX_AccountBalances_AccountId_CreatedOn ON AccountBalances (AccountId, CreatedOn DESC);
END
