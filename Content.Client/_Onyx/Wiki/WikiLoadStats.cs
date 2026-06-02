namespace Content.Client._Onyx.Wiki;

public readonly record struct WikiLoadStats(
    int LoadedArticles,
    int SkippedArticles,
    int TotalArticleBytes,
    bool HitArticleLimit,
    bool HitByteLimit);
