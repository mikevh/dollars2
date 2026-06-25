IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BudgetGroups')
BEGIN
    CREATE TABLE BudgetGroups (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        BudgetId INT NOT NULL,
        Name NVARCHAR(256) NOT NULL,
        IsIncome BIT NOT NULL DEFAULT 0,
        SortOrder INT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BudgetGroups_Budgets FOREIGN KEY (BudgetId) REFERENCES Budgets(Id)
    );
END
