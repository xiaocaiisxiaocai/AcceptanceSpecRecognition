using System.Reflection;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace AcceptanceSpecSystem.Data.Tests;

public sealed class DatabaseConstraintClassifierTests
{
    [Fact]
    public void 删除冲突分类器应识别EF并发异常()
    {
        DatabaseConstraintClassifier
            .IsDeleteConflict(new DbUpdateConcurrencyException("并发删除"))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(1451)]
    [InlineData(1217)]
    public void 删除冲突分类器应按MySql稳定错误码识别父项外键冲突(int errorCode)
    {
        var exception = new DbUpdateException(
            "删除失败",
            CreateMySqlException((MySqlErrorCode)errorCode, "provider detail"));

        DatabaseConstraintClassifier.IsDeleteConflict(exception).Should().BeTrue();
    }

    [Fact]
    public void 客户名冲突分类器应只接受目标唯一索引()
    {
        var target = new DbUpdateException(
            "保存失败",
            CreateMySqlException(
                MySqlErrorCode.DuplicateKeyEntry,
                "Duplicate entry '客户A' for key 'IX_Customers_Name'"));
        var other = new DbUpdateException(
            "保存失败",
            CreateMySqlException(
                MySqlErrorCode.DuplicateKeyEntry,
                "Duplicate entry '客户A' for key 'IX_Customers_Other'"));

        DatabaseConstraintClassifier
            .IsUniqueViolation(target, "IX_Customers_Name")
            .Should().BeTrue();
        DatabaseConstraintClassifier
            .IsUniqueViolation(other, "IX_Customers_Name")
            .Should().BeFalse();
    }

    [Fact]
    public void SQLite删除冲突应只按外键扩展错误码识别()
    {
        var foreignKey = new DbUpdateException(
            "删除失败",
            new SqliteException("provider detail", 19, 787));
        var checkConstraint = new DbUpdateException(
            "删除失败",
            new SqliteException("provider detail", 19, 275));

        DatabaseConstraintClassifier.IsDeleteConflict(foreignKey).Should().BeTrue();
        DatabaseConstraintClassifier.IsDeleteConflict(checkConstraint).Should().BeFalse();
    }

    [Fact]
    public void SQLite唯一冲突应按唯一扩展错误码识别()
    {
        var unique = new DbUpdateException(
            "保存失败",
            new SqliteException("provider detail", 19, 2067));
        var primaryKey = new DbUpdateException(
            "保存失败",
            new SqliteException("provider detail", 19, 1555));

        DatabaseConstraintClassifier
            .IsUniqueViolation(unique, "IX_Customers_Name")
            .Should().BeTrue();
        DatabaseConstraintClassifier
            .IsUniqueViolation(primaryKey, "IX_Customers_Name")
            .Should().BeFalse();
    }

    [Fact]
    public void 普通数据库错误不得分类为已知冲突()
    {
        var exception = new DbUpdateException("普通数据库错误");

        DatabaseConstraintClassifier.IsDeleteConflict(exception).Should().BeFalse();
        DatabaseConstraintClassifier
            .IsUniqueViolation(exception, "IX_Customers_Name")
            .Should().BeFalse();
    }

    private static MySqlException CreateMySqlException(MySqlErrorCode errorCode, string message)
    {
        var constructor = typeof(MySqlException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(MySqlErrorCode), typeof(string), typeof(string), typeof(Exception)],
            modifiers: null);

        constructor.Should().NotBeNull();
        return (MySqlException)constructor!.Invoke([errorCode, "23000", message, null]);
    }
}
