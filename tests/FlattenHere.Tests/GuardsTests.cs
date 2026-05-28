using FlattenHere;
using Xunit;

namespace FlattenHere.Tests;

public class GuardsTests
{
    private static readonly IReadOnlyList<GuardEntry> Sample = new[]
    {
        new GuardEntry(@"C:\Windows",                   "the Windows directory",     BlockSubfolders: true),
        new GuardEntry(@"C:\Program Files",             "Program Files",             BlockSubfolders: true),
        new GuardEntry(@"C:\Program Files (x86)",       "Program Files (x86)",       BlockSubfolders: true),
        new GuardEntry(@"C:\ProgramData",               "ProgramData",               BlockSubfolders: true),
        new GuardEntry(@"C:\Users\test",                "your user profile root",    BlockSubfolders: false),
        new GuardEntry(@"C:\Users\test\AppData\Roaming","the Roaming AppData root",  BlockSubfolders: false),
        new GuardEntry(@"C:\Users\test\AppData\Local",  "the Local AppData root",    BlockSubfolders: false),
        new GuardEntry(@"C:\$Recycle.Bin",              "the Recycle Bin",           BlockSubfolders: true),
        new GuardEntry(@"C:\System Volume Information", "System Volume Information", BlockSubfolders: true),
    };

    private const string SystemDrive = "C:";

    [Fact]
    public void Empty_path_is_blocked()
    {
        var r = Guards.Check("", Sample, SystemDrive);
        Assert.False(r.Allowed);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    public void Drive_root_is_blocked(string path)
    {
        var r = Guards.Check(path, Sample, SystemDrive);
        Assert.False(r.Allowed);
        Assert.Contains("root of a drive", r.Reason!);
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"c:\windows\system32\drivers")]
    public void System_root_and_its_subfolders_are_blocked(string path)
    {
        var r = Guards.Check(path, Sample, SystemDrive);
        Assert.False(r.Allowed);
    }

    [Theory]
    [InlineData(@"C:\Program Files\7-Zip")]
    [InlineData(@"C:\Program Files (x86)\Steam")]
    [InlineData(@"C:\ProgramData\chocolatey")]
    public void Program_dirs_block_subfolders(string path)
    {
        var r = Guards.Check(path, Sample, SystemDrive);
        Assert.False(r.Allowed);
    }

    [Fact]
    public void User_profile_root_is_blocked()
    {
        var r = Guards.Check(@"C:\Users\test", Sample, SystemDrive);
        Assert.False(r.Allowed);
    }

    [Theory]
    [InlineData(@"C:\Users\test\Documents")]
    [InlineData(@"C:\Users\test\Downloads\stuff")]
    [InlineData(@"C:\Users\test\AppData\Roaming\Some\Sub")]
    [InlineData(@"C:\Users\test\AppData\Local\Some\Sub")]
    public void Subfolders_of_user_profile_and_appdata_are_allowed(string path)
    {
        var r = Guards.Check(path, Sample, SystemDrive);
        Assert.True(r.Allowed, $"expected allow but got: {r.Reason}");
    }

    [Theory]
    [InlineData(@"C:\Users\test\AppData\Roaming")]
    [InlineData(@"C:\Users\test\AppData\Local")]
    public void AppData_roots_are_blocked(string path)
    {
        var r = Guards.Check(path, Sample, SystemDrive);
        Assert.False(r.Allowed);
    }

    [Theory]
    [InlineData(@"C:\$Recycle.Bin")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21-xxx")]
    [InlineData(@"C:\System Volume Information")]
    [InlineData(@"C:\System Volume Information\anything")]
    public void Recycle_bin_and_svi_are_blocked(string path)
    {
        var r = Guards.Check(path, Sample, SystemDrive);
        Assert.False(r.Allowed);
    }

    [Theory]
    [InlineData(@"C:\Temp")]
    [InlineData(@"C:\Photos")]
    public void Top_level_system_drive_folders_are_blocked(string path)
    {
        var r = Guards.Check(path, Sample, SystemDrive);
        Assert.False(r.Allowed);
        Assert.Contains("top-level system-drive folder", r.Reason!);
    }

    [Theory]
    [InlineData(@"C:\Photos\2024")]
    [InlineData(@"C:\Users\test\Downloads")]
    [InlineData(@"D:\Anything")]
    [InlineData(@"D:\Anything\Nested")]
    public void Normal_user_folders_are_allowed(string path)
    {
        var r = Guards.Check(path, Sample, SystemDrive);
        Assert.True(r.Allowed, $"expected allow but got: {r.Reason}");
    }

    [Fact]
    public void Path_is_normalized_before_comparison()
    {
        var r = Guards.Check(@"C:\Windows\..\Windows\System32", Sample, SystemDrive);
        Assert.False(r.Allowed);
    }
}
