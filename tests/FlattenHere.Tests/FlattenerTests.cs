using FlattenHere;
using Xunit;

namespace FlattenHere.Tests;

public sealed class FlattenerTests : IDisposable
{
    private readonly string _root;
    private readonly string _logPath;
    private readonly Logger _log;

    public FlattenerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "flatten-here-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _logPath = Path.Combine(_root, "test.log");
        _log = TestLogger.Open(_logPath);
    }

    public void Dispose()
    {
        _log.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string MakeFile(string relative, string content = "x")
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public void BuildPlan_collects_files_from_all_subfolders_but_not_target()
    {
        MakeFile(@"keep_at_top.txt");
        MakeFile(@"a\one.txt");
        MakeFile(@"a\b\two.txt");
        MakeFile(@"c\three.txt");

        var summary = new FlattenSummary();
        var plan = Flattener.BuildPlan(_root, _log, summary);

        Assert.Equal(3, plan.Count);
        Assert.Contains(plan, e => e.Source.EndsWith(@"a\one.txt"));
        Assert.Contains(plan, e => e.Source.EndsWith(@"a\b\two.txt"));
        Assert.Contains(plan, e => e.Source.EndsWith(@"c\three.txt"));
        Assert.DoesNotContain(plan, e => e.Source.EndsWith("keep_at_top.txt"));
    }

    [Fact]
    public void Execute_moves_files_into_target_and_clears_originals()
    {
        MakeFile(@"a\one.txt", "1");
        MakeFile(@"b\c\two.txt", "2");

        var summary = new FlattenSummary();
        var plan = Flattener.BuildPlan(_root, _log, summary);
        Flattener.Execute(plan, FlattenMode.Move, _log, summary);

        Assert.True(File.Exists(Path.Combine(_root, "one.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "two.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "a", "one.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "b", "c", "two.txt")));
        Assert.Equal(2, summary.Moved);
        Assert.Equal(0, summary.Renamed);
        Assert.Equal(0, summary.Failed);
    }

    [Fact]
    public void Colliding_names_get_numeric_rename()
    {
        MakeFile(@"a\photo.jpg", "A");
        MakeFile(@"b\photo.jpg", "B");
        MakeFile(@"c\photo.jpg", "C");

        var summary = new FlattenSummary();
        var plan = Flattener.BuildPlan(_root, _log, summary);
        Flattener.Execute(plan, FlattenMode.Move, _log, summary);

        Assert.True(File.Exists(Path.Combine(_root, "photo.jpg")));
        Assert.True(File.Exists(Path.Combine(_root, "photo (2).jpg")));
        Assert.True(File.Exists(Path.Combine(_root, "photo (3).jpg")));
        Assert.Equal(1, summary.Moved);
        Assert.Equal(2, summary.Renamed);
    }

    [Fact]
    public void Existing_top_level_file_is_not_overwritten()
    {
        MakeFile("photo.jpg", "TOP");
        MakeFile(@"a\photo.jpg", "FROM_A");

        var summary = new FlattenSummary();
        var plan = Flattener.BuildPlan(_root, _log, summary);
        Flattener.Execute(plan, FlattenMode.Move, _log, summary);

        Assert.Equal("TOP", File.ReadAllText(Path.Combine(_root, "photo.jpg")));
        Assert.Equal("FROM_A", File.ReadAllText(Path.Combine(_root, "photo (2).jpg")));
    }

    [Fact]
    public void DryRun_does_not_touch_the_filesystem()
    {
        var src = MakeFile(@"a\b\thing.txt", "data");

        var summary = new FlattenSummary();
        var plan = Flattener.BuildPlan(_root, _log, summary);
        Flattener.Execute(plan, FlattenMode.DryRun, _log, summary);

        Assert.True(File.Exists(src), "Original must still exist in dry-run.");
        Assert.False(File.Exists(Path.Combine(_root, "thing.txt")));
        Assert.Equal(1, summary.Moved);
    }

    [Fact]
    public void CleanupEmpty_removes_empty_subfolders_bottom_up()
    {
        MakeFile(@"a\b\c\deep.txt");

        var summary = new FlattenSummary();
        var plan = Flattener.BuildPlan(_root, _log, summary);
        Flattener.Execute(plan, FlattenMode.Move, _log, summary);
        Flattener.CleanupEmpty(_root, FlattenMode.Move, _log, summary);

        Assert.False(Directory.Exists(Path.Combine(_root, "a", "b", "c")));
        Assert.False(Directory.Exists(Path.Combine(_root, "a", "b")));
        Assert.False(Directory.Exists(Path.Combine(_root, "a")));
        Assert.Equal(3, summary.RemovedDirs);
    }

    [Fact]
    public void CleanupEmpty_keeps_folders_that_still_have_files()
    {
        // Exercises CleanupEmpty in isolation. After a real Execute() every subdir is
        // drained, so a "still has files" case only matters if a file shows up between
        // the move pass and the cleanup pass (e.g. another process writes to a subdir).
        Directory.CreateDirectory(Path.Combine(_root, "empty-only", "deeper-empty"));
        MakeFile(@"has-file\stay.txt", "keep");
        MakeFile(@"mixed\sub\stay.txt", "keep");
        Directory.CreateDirectory(Path.Combine(_root, "mixed", "empty-sibling"));

        var summary = new FlattenSummary();
        Flattener.CleanupEmpty(_root, FlattenMode.Move, _log, summary);

        Assert.False(Directory.Exists(Path.Combine(_root, "empty-only", "deeper-empty")));
        Assert.False(Directory.Exists(Path.Combine(_root, "empty-only")));
        Assert.False(Directory.Exists(Path.Combine(_root, "mixed", "empty-sibling")));
        Assert.True(Directory.Exists(Path.Combine(_root, "has-file")));
        Assert.True(Directory.Exists(Path.Combine(_root, "mixed", "sub")));
        Assert.True(Directory.Exists(Path.Combine(_root, "mixed")));
        Assert.Equal(3, summary.RemovedDirs);
    }

    [Fact]
    public void CountSubfoldersDeep_counts_every_descendant()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a", "b", "c"));
        Directory.CreateDirectory(Path.Combine(_root, "x"));

        var n = Flattener.CountSubfoldersDeep(_root);
        Assert.Equal(4, n);
    }

    [Fact]
    public void BuildPlan_throws_when_target_does_not_exist()
    {
        var missing = Path.Combine(_root, "no-such-dir");
        var summary = new FlattenSummary();
        Assert.Throws<DirectoryNotFoundException>(() => Flattener.BuildPlan(missing, _log, summary));
    }

    private static class TestLogger
    {
        public static Logger Open(string path)
        {
            // Logger always wants a folder. Make sure parent exists then call the
            // normal Open() pointed at a sibling location; Logger appends to
            // flatten-here.log inside the folder we pass.
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            return Logger.Open(dir);
        }
    }
}
