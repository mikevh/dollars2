IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '004_create_budget_groups')
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

    INSERT INTO Migrations (ScriptName) VALUES ('004_create_budget_groups');
END
