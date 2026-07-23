IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Accounts') AND name = 'IncludeInBudget'
)
BEGIN
    ALTER TABLE Accounts ADD IncludeInBudget bit NOT NULL DEFAULT 1;
END
