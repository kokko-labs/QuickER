using QuickER.Model;

namespace QuickER.Provider;

/// <summary>ER 図から CREATE TABLE / FK 制約の DDL を生成（DB 方言ごとに実装）</summary>
public interface IDdlGenerator
{
    /// <summary>ER 図定義から対象方言の DDL 文字列を生成する</summary>
    string Build(ErDiagram diagram);
}
