using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// <see cref="Entity.IsCandidateKey"/>（外部キーが参照する親側の列が、主キーか一意制約とちょうど一致するか）を検証するテストクラス
/// </summary>
public class EntityCandidateKeyTests
{
    private readonly Column _tenantId = new() { Name = "TenantId", IsPrimaryKey = true };
    private readonly Column _projectId = new() { Name = "ProjectId", IsPrimaryKey = true };
    private readonly Column _code = new() { Name = "Code" };
    private readonly Column _label = new() { Name = "Label" };

    /// <summary>複合主キー (TenantId, ProjectId) と一意制約 (Code) を持つテーブル</summary>
    private Entity BuildEntity() =>
        new()
        {
            TableName = "Project",
            Columns = [_tenantId, _projectId, _code, _label],
            UniqueConstraints = [new UniqueConstraint { ColumnIds = [_code.Id] }],
        };

    [Fact(DisplayName = "主キー全列と一致すれば候補キー（並びは問わない）")]
    public void PrimaryKeyColumns_InAnyOrder_AreCandidateKey()
    {
        var entity = BuildEntity();

        entity.IsCandidateKey([_projectId.Id, _tenantId.Id]).Should().BeTrue();
    }

    [Fact(DisplayName = "一意制約の列と一致すれば候補キー")]
    public void UniqueConstraintColumns_AreCandidateKey()
    {
        BuildEntity().IsCandidateKey([_code.Id]).Should().BeTrue();
    }

    [Fact(DisplayName = "主キーの一部・一意でない列・主キーに余分な列を足した組は候補キーでない")]
    public void PartialOrExtraColumns_AreNotCandidateKey()
    {
        var entity = BuildEntity();

        entity.IsCandidateKey([_tenantId.Id]).Should().BeFalse("主キーの一部");
        entity.IsCandidateKey([_label.Id]).Should().BeFalse("一意でない列");
        entity
            .IsCandidateKey([_tenantId.Id, _projectId.Id, _label.Id])
            .Should()
            .BeFalse("主キーに余分な列を足した組");
    }

    [Fact(DisplayName = "空の集合は、主キーの無いテーブルでも候補キーでない")]
    public void EmptySet_IsNotCandidateKey()
    {
        new Entity { Columns = [_label] }
            .IsCandidateKey([])
            .Should()
            .BeFalse();
        BuildEntity().IsCandidateKey([]).Should().BeFalse();
    }
}
