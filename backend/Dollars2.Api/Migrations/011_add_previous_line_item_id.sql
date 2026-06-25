IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('LineItems') AND name = 'PreviousLineItemId'
)
BEGIN
    ALTER TABLE LineItems ADD PreviousLineItemId INT NULL;

    ALTER TABLE LineItems ADD CONSTRAINT FK_LineItems_PreviousLineItem
        FOREIGN KEY (PreviousLineItemId) REFERENCES LineItems(Id);
END
