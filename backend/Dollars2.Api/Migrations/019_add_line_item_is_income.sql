IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '019_add_line_item_is_income')
BEGIN
    ALTER TABLE LineItems ADD IsIncome BIT NOT NULL CONSTRAINT DF_LineItems_IsIncome DEFAULT 0;

    INSERT INTO Migrations (ScriptName) VALUES ('019_add_line_item_is_income');
END
