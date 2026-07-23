IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '011_add_previous_line_item_id')
BEGIN
    ALTER TABLE LineItems ADD PreviousLineItemId INT NULL;

    ALTER TABLE LineItems ADD CONSTRAINT FK_LineItems_PreviousLineItem
        FOREIGN KEY (PreviousLineItemId) REFERENCES LineItems(Id);

    INSERT INTO Migrations (ScriptName) VALUES ('011_add_previous_line_item_id');
END
