using Bao1702.Codeplug.Binary;
using Bao1702.Codeplug.Model;

namespace Bao1702.Codeplug.Validation;

/// <summary>Result of validating a codeplug image prior to radio write, with issue list and size check.</summary>
public sealed record CodeplugWriteValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Issues,
    int ExpectedImageSize,
    int ActualImageSize);

public static class CodeplugWriteValidator
{
    public static CodeplugWriteValidationResult ValidateModel(CodeplugImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var issues = CodeplugValidator.Validate(image).ToList();
        return new CodeplugWriteValidationResult(
            issues.All(issue => issue.Severity != ValidationSeverity.Error),
            issues,
            ExpectedImageSize: 0,
            ActualImageSize: image.PreservedRawImage.Length);
    }

    public static CodeplugWriteValidationResult ValidateImage(ReadOnlyMemory<byte> imageBytes, int expectedImageSize)
    {
        if (expectedImageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedImageSize));
        }

        var issues = new List<ValidationIssue>();
        if (imageBytes.Length != expectedImageSize)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                $"Codeplug image length {imageBytes.Length} does not match the expected target size {expectedImageSize}."));
        }

        try
        {
            var image = CodeplugBinarySerializer.Deserialize(imageBytes.Span);
            issues.AddRange(CodeplugValidator.Validate(image));
            if (image.PreservedRawImage.Length != 0 && image.PreservedRawImage.Length != imageBytes.Length)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "Serialized container raw image length differs from the supplied image length."));
            }
        }
        catch (InvalidDataException)
        {
            if (imageBytes.Length == expectedImageSize)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "Image is not the provisional Bao1702Suite container format. Treating it as an opaque raw codeplug blob."));
            }
        }

        return new CodeplugWriteValidationResult(
            issues.All(issue => issue.Severity != ValidationSeverity.Error),
            issues,
            expectedImageSize,
            imageBytes.Length);
    }
}
