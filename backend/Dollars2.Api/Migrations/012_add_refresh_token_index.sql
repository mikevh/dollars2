IF NOT EXISTS (SELECT * FROM Migrations WHERE ScriptName = '012_add_refresh_token_index')
BEGIN
    CREATE NONCLUSTERED INDEX IX_RefreshTokens_Token
        ON RefreshTokens (Token);

    INSERT INTO Migrations (ScriptName) VALUES ('012_add_refresh_token_index');
END
