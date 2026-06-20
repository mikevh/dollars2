IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '007_create_transactions')
BEGIN
    CREATE TABLE Transactions (
        Id int IDENTITY(1,1) PRIMARY KEY,
        AccountId int NULL REFERENCES Accounts(Id),
        UserId int NOT NULL REFERENCES Users(Id),
        ProviderTransactionId nvarchar(500) NULL,
        Date date NOT NULL,
        Description nvarchar(500) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        Notes nvarchar(max) NULL,
        IsDeleted bit NOT NULL DEFAULT 0,
        IsPending bit NOT NULL DEFAULT 0,
        IsManual bit NOT NULL DEFAULT 0,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_Transactions_Provider
        ON Transactions(AccountId, ProviderTransactionId)
        WHERE ProviderTransactionId IS NOT NULL;

    INSERT INTO Migrations (ScriptName) VALUES ('007_create_transactions');
END
