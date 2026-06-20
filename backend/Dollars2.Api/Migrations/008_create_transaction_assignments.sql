IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '008_create_transaction_assignments')
BEGIN
    CREATE TABLE TransactionAssignments (
        Id int IDENTITY(1,1) PRIMARY KEY,
        TransactionId int NOT NULL REFERENCES Transactions(Id),
        LineItemId int NOT NULL REFERENCES LineItems(Id),
        Amount decimal(18,2) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );

    INSERT INTO Migrations (ScriptName) VALUES ('008_create_transaction_assignments');
END
