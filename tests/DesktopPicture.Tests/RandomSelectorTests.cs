using System;
using System.Collections.Generic;
using System.Linq;
using DesktopPicture.Random;
using Xunit;

namespace DesktopPicture.Tests;

public class RandomSelectorTests
{
    [Fact]
    public void Test_EmptyCandidates_ReturnsNull()
    {
        var selector = new RandomSelector();
        var result = selector.SelectNext(Array.Empty<string>(), null);
        Assert.Null(result);
    }

    [Fact]
    public void Test_SingleCandidate_ReturnsSame()
    {
        var selector = new RandomSelector();
        var candidates = new[] { "C:\\pic1.jpg" };

        var result1 = selector.SelectNext(candidates, null);
        var result2 = selector.SelectNext(candidates, "C:\\pic1.jpg");

        Assert.Equal("C:\\pic1.jpg", result1);
        Assert.Equal("C:\\pic1.jpg", result2);
    }

    [Fact]
    public void Test_TwoCandidates_NeverAdjacentDuplicate()
    {
        var selector = new RandomSelector();
        var candidates = new[] { "C:\\pic1.jpg", "C:\\pic2.jpg" };

        string? current = candidates[0];
        for (int i = 0; i < 1000; i++)
        {
            var next = selector.SelectNext(candidates, current);
            Assert.NotNull(next);
            Assert.NotEqual(current, next);
            current = next;
        }
    }

    [Fact]
    public void Test_LargeCandidateSet_NeverAdjacentDuplicate()
    {
        var selector = new RandomSelector();
        var candidates = Enumerable.Range(1, 500).Select(i => $"C:\\Pictures\\photo_{i}.jpg").ToArray();

        string? current = null;
        for (int i = 0; i < 5000; i++)
        {
            var next = selector.SelectNext(candidates, current);
            Assert.NotNull(next);
            if (current != null)
            {
                Assert.NotEqual(current, next);
            }
            current = next;
        }
    }

    [Fact]
    public void Test_UniformDistribution()
    {
        var selector = new RandomSelector();
        var candidates = new[] { "A", "B", "C", "D" };
        var counts = new Dictionary<string, int> { ["A"] = 0, ["B"] = 0, ["C"] = 0, ["D"] = 0 };

        string? current = "A";
        int totalIterations = 12000;

        for (int i = 0; i < totalIterations; i++)
        {
            var next = selector.SelectNext(candidates, current);
            Assert.NotNull(next);
            Assert.NotEqual(current, next);
            counts[next]++;
            current = next;
        }

        // When starting at A, each element except previous should have roughly equal probability ~1/3
        foreach (var (k, count) in counts)
        {
            Assert.True(count > 2000, $"Key {k} count {count} is too low");
        }
    }
}
