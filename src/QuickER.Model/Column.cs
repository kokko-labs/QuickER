namespace QuickER.Model;

/// <summary>
/// テーブル内の 1 カラムを表すモデル
/// JSON シリアライズの対象となる単純な POCO
/// </summary>
public class Column
{
    /// <summary>カラムの一意識別子</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _name = "NewColumn";

    /// <summary>カラム名（例: <c>顧客ID</c>）</summary>
    /// <remarks>
    /// 代入した名前の前後の空白は除く（<see cref="Entity.TableName"/> と同じ規則）。<c>null</c> はそのまま通す
    /// （手で編集した JSON の <c>null</c> は、読込の必須名の検査が場所を名指しして断る）。
    /// </remarks>
    public string Name
    {
        get => _name;
        set => _name = value?.Trim()!;
    }

    /// <summary>SQL のデータ型（例: <c>int</c>, <c>varchar(100)</c>）</summary>
    public string DataType { get; set; } = "int";

    /// <summary>主キーかどうかを示す</summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>外部キーかどうかを示す</summary>
    public bool IsForeignKey { get; set; }

    /// <summary>NULL を許容するかどうかを示す</summary>
    public bool IsNullable { get; set; } = true;

    /// <summary>カラムの説明（SQL Server の拡張プロパティ <c>MS_Description</c> と同期する）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 計算列・生成列かどうかを示す（DB が式から値を作り、明示の書き込みを受け付けない列）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQL Server の計算列（<c>AS (式)</c>）・MySQL / PostgreSQL / SQLite の生成列
    /// （<c>GENERATED ALWAYS AS</c>）・Oracle の仮想列が該当する。
    /// </para>
    /// <para>
    /// <b>式そのものは意味モデルに載らない</b>＝この図から生成する DDL は普通の列を作る。
    /// 運ぶのは「書き込みを受け付けない」という事実だけで、生成コードはその列を
    /// INSERT / UPDATE の対象から外す。
    /// </para>
    /// </remarks>
    public bool IsComputed { get; set; }

    /// <summary>
    /// DB が値を採番し、指定した値を拒む列か（SQL Server の IDENTITY・PostgreSQL / Oracle の
    /// <c>GENERATED ALWAYS AS IDENTITY</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 採番の定義（開始値・増分）は意味モデルに載らない＝この図から生成する DDL は普通の列を作る。
    /// 運ぶのは「DB が採番し、明示の書き込みを受け付けない」という事実だけで、生成コードは主キーでないこの列を
    /// INSERT / UPDATE の対象から外す（主キーの列は外さず、生成時に警告する）。
    /// </para>
    /// <para>
    /// 指定した値も受け付ける自動採番（MySQL の AUTO_INCREMENT など）は印を付けない（取込が告げるだけ）。
    /// </para>
    /// </remarks>
    public bool IsIdentity { get; set; }

    /// <summary>カラムを複製する</summary>
    /// <param name="preserveId"><c>true</c> の場合は同じ ID を維持し、<c>false</c> の場合は新しい ID を割り当てる</param>
    /// <returns>複製された <see cref="Column"/></returns>
    public Column Clone(bool preserveId) =>
        new()
        {
            Id = preserveId ? Id : Guid.NewGuid(),
            Name = Name,
            DataType = DataType,
            IsPrimaryKey = IsPrimaryKey,
            IsForeignKey = IsForeignKey,
            IsNullable = IsNullable,
            Description = Description,
            IsComputed = IsComputed,
            IsIdentity = IsIdentity,
        };
}
