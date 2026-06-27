using System.IO;
using System.Text.Json;

namespace ERDesigner.Services;

/// <summary>C# コード生成ダイアログの設定（次回起動時に復元する永続化対象）</summary>
public class CSharpGenerationSettings
{
    /// <summary>出力をカテゴリごとに別ファイル・別名前空間へ分割するか（false=1ファイルにまとめる）</summary>
    public bool SplitFilesByCategory { get; set; }

    /// <summary>ベース（ルート）名前空間。分割時は各カテゴリ名前空間のフォールバック元になる</summary>
    public string BaseNamespace { get; set; } = DefaultBaseNamespace;

    /// <summary>分割時の共有基盤（Runtime）名前空間。空なら {base}.Runtime にフォールバック</summary>
    public string RuntimeNamespace { get; set; } = string.Empty;

    /// <summary>分割時の Entity 名前空間。空なら {base}.Entities にフォールバック</summary>
    public string EntityNamespace { get; set; } = string.Empty;

    /// <summary>分割時の EditModel 名前空間。空なら {base}.EditModels にフォールバック</summary>
    public string EditModelNamespace { get; set; } = string.Empty;

    /// <summary>分割時の Mapper 名前空間。空なら {base}.Mappers にフォールバック</summary>
    public string MapperNamespace { get; set; } = string.Empty;

    /// <summary>分割時の Repository 名前空間。空なら {base}.Repositories にフォールバック</summary>
    public string RepositoryNamespace { get; set; } = string.Empty;

    /// <summary>分割時の ValueObject 名前空間。空なら {base}.ValueObjects にフォールバック</summary>
    public string ValueObjectNamespace { get; set; } = string.Empty;

    /// <summary>Entity クラスを生成するか</summary>
    public bool GenerateEntityClasses { get; set; } = true;

    /// <summary>EditModel クラスを生成するか</summary>
    public bool GenerateEditModels { get; set; } = true;

    /// <summary>Mapper クラスを生成するか</summary>
    public bool GenerateMappers { get; set; } = true;

    /// <summary>Repository クラスを生成するか</summary>
    public bool GenerateRepositories { get; set; } = true;

    /// <summary>全カラムを値オブジェクト化するか</summary>
    public bool GenerateValueObjects { get; set; }

    /// <summary>string 主キーを GuidKey 値オブジェクト化するか</summary>
    public bool UseGuidKeyForStringPrimaryKey { get; set; }

    /// <summary>非分割（モード①）時の出力ファイルパス</summary>
    public string OutputFilePath { get; set; } = DefaultOutputFilePath;

    /// <summary>分割（モード②）時の出力フォルダパス</summary>
    public string OutputFolderPath { get; set; } = string.Empty;

    /// <summary>ベース名前空間の工場出荷既定（分割時は {base}.Entities 等のフォールバック元になるため接尾辞なし）</summary>
    public const string DefaultBaseNamespace = "Generated";

    /// <summary>非分割時の出力ファイル名の工場出荷既定</summary>
    public const string DefaultOutputFilePath = "ErDesignerEntities.g.cs";

    /// <summary>工場出荷既定の設定を生成する（クリア／初回起動で使う）</summary>
    public static CSharpGenerationSettings CreateDefault() => new();
}

/// <summary>C# コード生成ダイアログ設定を JSON ファイルへ保存・読込するストア</summary>
public class CSharpGenerationSettingsStore
{
    /// <summary>JSON シリアライズ設定（インデント付与・プロパティ名は camelCase）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>設定ファイルの保存先フォルダ</summary>
    private readonly string _folder;

    /// <summary>既定の保存先（%APPDATA%\ERDesigner）で設定ストアを生成する</summary>
    public CSharpGenerationSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ERDesigner"
            )
        ) { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    public CSharpGenerationSettingsStore(string folder)
    {
        _folder = folder;
    }

    /// <summary>設定ファイルの絶対パス</summary>
    public string SettingsPath => Path.Combine(_folder, "csharp-generation.json");

    /// <summary>設定を読み込む（ファイルが無い・解析失敗時は既定値を返す）</summary>
    public CSharpGenerationSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return CSharpGenerationSettings.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<CSharpGenerationSettings>(json, JsonOptions)
                ?? CSharpGenerationSettings.CreateDefault();
        }
        catch
        {
            // 破損ファイル等で起動を妨げないよう既定値へフォールバックする
            return CSharpGenerationSettings.CreateDefault();
        }
    }

    /// <summary>設定を保存する（保存先フォルダが無ければ作成する）</summary>
    public void Save(CSharpGenerationSettings settings)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
