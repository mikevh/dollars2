IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '014_add_transaction_payee_memo')
BEGIN
    ALTER TABLE Transactions ADD Payee nvarchar(500) NOT NULL;

    ALTER TABLE Transactions ADD Memo nvarchar(500) NOT NULL;

    INSERT INTO Migrations (ScriptName) VALUES ('014_add_transaction_payee_memo');
END
