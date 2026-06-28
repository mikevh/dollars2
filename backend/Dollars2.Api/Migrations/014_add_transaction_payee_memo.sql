IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Transactions') AND name = 'Payee'
)
BEGIN
    ALTER TABLE Transactions ADD Payee nvarchar(500) NOT NULL;
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Transactions') AND name = 'Memo'
)
BEGIN
    ALTER TABLE Transactions ADD Memo nvarchar(500) NOT NULL;
END
