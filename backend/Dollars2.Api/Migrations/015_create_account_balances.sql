IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '015_create_account_balances')
BEGIN
    CREATE TABLE AccountBalances (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        AccountId INT NOT NULL,
        Balance DECIMAL(18,2) NOT NULL,
        CreatedOn DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedOn DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_AccountBalances_Accounts FOREIGN KEY (AccountId) REFERENCES Accounts(Id)
    );

    CREATE INDEX IX_AccountBalances_AccountId_CreatedOn ON AccountBalances (AccountId, CreatedOn DESC);

    INSERT INTO Migrations (ScriptName) VALUES ('015_create_account_balances');
END
