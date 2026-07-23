IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '005_create_line_items')
BEGIN
    CREATE TABLE LineItems (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        GroupId INT NOT NULL,
        Name NVARCHAR(256) NOT NULL,
        PlannedAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        SortOrder INT NOT NULL DEFAULT 0,
        Notes NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_LineItems_BudgetGroups FOREIGN KEY (GroupId) REFERENCES BudgetGroups(Id)
    );

    INSERT INTO Migrations (ScriptName) VALUES ('005_create_line_items');
END
