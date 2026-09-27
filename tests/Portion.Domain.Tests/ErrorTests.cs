using Portion.Domain.Common;
using Portion.Domain.Enums;

namespace Portion.Domain.Tests;

/// <summary>
/// Pins the error-to-status mapping and the persisted status values.
/// </summary>
/// <remarks>
/// The status ordinals are asserted explicitly because the existing SQLite database stores them as
/// integers. Reordering the enum would silently reinterpret every row in <c>portion_local.db</c>.
/// </remarks>
public class ErrorTests
{
    [Fact]
    public void NotFound_IsClassifiedAsNotFound()
    {
        Assert.Equal(ErrorType.NotFound, Error.NotFound("resumes.not-found", "missing").Type);
    }

    [Fact]
    public void Validation_RetainsFieldErrors()
    {
        var errors = new Dictionary<string, string[]> { ["file"] = ["required"] };

        var error = Error.Validation("resumes.upload.missing-file", "missing", errors);

        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(["required"], error.FieldErrors["file"]);
    }

    [Fact]
    public void Validation_DropsAnEmptyFieldErrorMap_SoTheErrorStaysCompact()
    {
        var error = Error.Validation("code", "description", new Dictionary<string, string[]>());

        Assert.Null(error.ValidationErrors);
        Assert.Empty(error.FieldErrors);
    }

    [Fact]
    public void None_IsNotAFailure()
    {
        Assert.True(Error.None.IsNone);
        Assert.False(Error.None.IsFailure);
    }

    [Fact]
    public void FieldErrors_ReturnsAnEmptyMap_WhenAbsent_SoCallersNeedNoNullCheck()
    {
        Assert.Empty(Error.NotFound("code", "description").FieldErrors);
    }

    [Theory]
    [InlineData(IngestionStatus.Discovered, 0)]
    [InlineData(IngestionStatus.Processing, 1)]
    [InlineData(IngestionStatus.Synced, 2)]
    [InlineData(IngestionStatus.Failed, 3)]
    public void IngestionStatus_OrdinalsAreFrozen_ForCompatibilityWithTheExistingDatabase(
        IngestionStatus status,
        int expected)
    {
        Assert.Equal(expected, (int)status);
    }
}
