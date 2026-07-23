IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '016_add_account_include_in_budget')
BEGIN
    ALTER TABLE Accounts ADD IncludeInBudget bit NOT NULL DEFAULT 1;

    INSERT INTO Migrations (ScriptName) VALUES ('016_add_account_include_in_budget');
END
