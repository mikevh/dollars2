IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '003_create_budgets')
BEGIN
    CREATE TABLE Budgets (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL,
        [Year] INT NOT NULL,
        [Month] INT NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Budgets_Users FOREIGN KEY (UserId) REFERENCES Users(Id),
        CONSTRAINT UQ_Budgets_UserId_Year_Month UNIQUE (UserId, [Year], [Month])
    );

    INSERT INTO Migrations (ScriptName) VALUES ('003_create_budgets');
END
