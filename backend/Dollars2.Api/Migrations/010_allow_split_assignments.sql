IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '010_allow_split_assignments')
BEGIN
    DROP INDEX IF EXISTS UX_TransactionAssignments_TransactionId ON TransactionAssignments;

    CREATE UNIQUE INDEX UX_TransactionAssignments_TransactionId_LineItemId
        ON TransactionAssignments (TransactionId, LineItemId);

    INSERT INTO Migrations (ScriptName) VALUES ('010_allow_split_assignments');
END
