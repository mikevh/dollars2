IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('RefreshTokens') AND name = 'IX_RefreshTokens_Token'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_RefreshTokens_Token
        ON RefreshTokens (Token);
END
