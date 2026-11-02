using Mawaqit.Core;
using Xunit;

namespace Mawaqit.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mawaqit-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Atomic_write_replaces_and_leaves_no_temp_files()
    {
        var path = Path.Combine(_dir, "sub", "config.toml");
        AtomicFile.WriteAllText(path, "one");
        AtomicFile.WriteAllText(path, "two ✓");

        Assert.Equal("two ✓", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]); // no BOM: the Rust build reads this file too
    }

    [Fact]
    public void Corrupt_file_is_moved_aside()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "config.toml");
        File.WriteAllText(path, "garbage");
        var moved = AtomicFile.QuarantineCorrupt(path, new DateTimeOffset(2026, 11, 1, 23, 0, 0, TimeSpan.Zero));

        Assert.False(File.Exists(path));
        Assert.Equal("garbage", File.ReadAllText(moved!));
        Assert.EndsWith(".broken-20261101-230000", moved);
    }

    [Fact]
    public void Timetable_round_trips_through_the_cache()
    {
        var (table, _) = TestData.Parse("montreal-assahaba", "America/Toronto");
        table.Slug = "assahaba-mtl";
        var store = new TimetableStore(_dir);

        store.Save(table);
        var back = store.Load("assahaba-mtl")!;

        Assert.Equal(366, back.Days.Count);
        Assert.Equal(table.TimesFor(new DateOnly(2026, 11, 1)), back.TimesFor(new DateOnly(2026, 11, 1)));
        Assert.Equal(table.IqamaFor(new DateOnly(2026, 11, 1)), back.IqamaFor(new DateOnly(2026, 11, 1)));
        Assert.Equal(table.FetchedAt, back.FetchedAt);
        Assert.Equal(table.Latitude, back.Latitude);
    }

    [Fact]
    public void Cache_misses_instead_of_throwing()
    {
        var store = new TimetableStore(_dir);
        Assert.Null(store.Load("never-saved"));
        Assert.Null(store.Load("../../etc/passwd"));

        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "torn.json"), "{\"Slug\":\"torn\",\"Days\":{");
        Assert.Null(store.Load("torn"));
    }

    [Fact]
    public void Unsafe_slugs_never_become_paths()
    {
        var store = new TimetableStore(_dir);
        Assert.Null(store.PathFor("..\\evil"));
        Assert.Throws<ArgumentException>(() => store.Save(new MosqueTimetable { Slug = "a/b" }));
    }
}
