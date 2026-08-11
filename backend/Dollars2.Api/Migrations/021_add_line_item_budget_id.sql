IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '021_add_line_item_budget_id')
BEGIN
    ALTER TABLE LineItems ADD BudgetId INT NULL;

    INSERT INTO Migrations (ScriptName) VALUES ('021_add_line_item_budget_id');
END
