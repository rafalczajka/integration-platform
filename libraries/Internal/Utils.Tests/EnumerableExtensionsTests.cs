using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Integrations.Utils.Tests;

public sealed class EnumerableExtensionsTests
{
    [Fact]
    public void AsReadOnlyCollection_Array_ReturnsSameInstance()
    {
        int[] source = [3, 1, 3];

        Assert.Same(source, source.AsReadOnlyCollection());
    }

    [Fact]
    public void AsReadOnlyCollection_List_ReusesSourceAndReflectsChanges()
    {
        List<string> source = ["first"];
        var result = source.AsReadOnlyCollection();

        source.Add("second");

        Assert.Same(source, result);
        Assert.Equal(2, result.Count);
        Assert.Equal(["first", "second"], result);
    }

    [Fact]
    public void AsReadOnlyCollection_Iterator_MaterializesOnceAndPreservesOrderAndDuplicates()
    {
        var enumerationCount = 0;

        IEnumerable<int> GetItems()
        {
            enumerationCount++;
            yield return 3;
            yield return 1;
            yield return 3;
        }

        var result = GetItems().AsReadOnlyCollection();

        Assert.Equal(1, enumerationCount);
        Assert.Equal(3, result.Count);
        Assert.Equal([3, 1, 3], result);
        Assert.Equal([3, 1, 3], result);
        Assert.Equal(1, enumerationCount);
    }

    [Fact]
    public void AsReadOnlyCollection_EmptyIterator_ReturnsEmptyCollection()
    {
        var result = Enumerable.Range(0, 0).Select(value => value).AsReadOnlyCollection();

        Assert.Empty(result);
    }

    [Fact]
    public void AsReadOnlyCollection_Null_Throws()
    {
        IEnumerable<int> source = null!;

        Assert.Throws<ArgumentNullException>(() => source.AsReadOnlyCollection());
    }
}
