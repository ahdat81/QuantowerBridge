using System;
using System.Collections.Generic;
using Xunit;

public class IsObjDefaultTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(5, false)]
    public void IsObjDefault_IntTests(int input, bool expected)
    {
        bool result = IsObjDefault<int>(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(3.14, false)]
    public void IsObjDefault_DoubleTests(double input, bool expected)
    {
        bool result = IsObjDefault<double>(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void IsObjDefault_BoolTests(bool input, bool expected)
    {
        bool result = IsObjDefault<bool>(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsObjDefault_NullObject_ReturnsFalse()
    {
        object obj = null;
        bool result = IsObjDefault<int>(obj);
        Assert.False(result);
    }

    private static bool IsObjDefault<T>(object obj) where T : struct
    {
        if (obj is T value)
        {
            return EqualityComparer<T>.Default.Equals(value, default);
        }
        else
        {
            return false;
        }
    }
}