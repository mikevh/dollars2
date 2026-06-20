IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '009_add_unique_transaction_assignment')
BEGIN
    CREATE UNIQUE INDEX UX_TransactionAssignments_TransactionId
        ON TransactionAssignments (TransactionId);

    INSERT INTO Migrations (ScriptName) VALUES ('009_add_unique_transaction_assignment');
END
