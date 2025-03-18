using System;
using Xunit;

public class IsGuidTests
{
    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000", true)] // Valid GUID
    [InlineData("12345678-1234-5678-1234-567812345678", true)] // Valid GUID
    [InlineData("550e8400-e29b-41d4-a716-44665544000", false)] // Too short
    [InlineData("550e8400-e29b-41d4-a716-4466554400000", false)] // Too long
    [InlineData("550e8400e29b41d4a716446655440000", false)] // Missing dashes
    [InlineData("ZZZZZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZZZZZZZZZ", false)] // Invalid characters
    [InlineData("", false)] // Empty string
    [InlineData(null, false)] // Null string
    public void IsGuid_ShouldReturnExpected(string input, bool expected)
    {
        bool result = IsGuid(input);
        Assert.Equal(expected, result);
    }

    private static bool IsGuid(string str)
    {
        return Guid.TryParse(str, out _);
    }
}