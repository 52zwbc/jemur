using System.IO;
using Jemur.Core;
using Xunit;

namespace Jemur.Tests;

/// <summary>Desktop pins and card groups in the Library.</summary>
[Collection("AppPaths")]
public sealed class LibraryGroupPinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jemur-tests-" + Guid.NewGuid().ToString("N"));

    public LibraryGroupPinTests()
    {
        AppPaths.OverrideRoot(_root);
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static Library Loaded()
    {
        var lib = new Library();
        lib.Load(7);
        return lib;
    }

    private static HangItem Add(Library lib, string? rope = null)
    {
        var n = new HangItem { Kind = ItemKind.Note, Text = "x", RopeId = rope };
        lib.Add(n);
        return n;
    }

    [Fact]
    public void Load_SurvivesNullAndDuplicateGroups()
    {
        File.WriteAllText(AppPaths.ItemsFile, """
            {"Version":2,"Ropes":[{"Id":"default","Name":"Jemuran"}],
             "Groups":[null,{"Id":null,"ItemIds":["n1"]},{"Id":"g","Name":null,"ItemIds":["n1",null]},{"Id":"g","ItemIds":["n2"]}],
             "Items":[{"Id":"n1","Kind":"Note","Text":"a"},{"Id":"n2","Kind":"Note","Text":"b"}],"Discarded":[]}
            """);
        var lib = Loaded();
        var g = Assert.Single(lib.Groups);
        Assert.Equal("g", g.Id);
        Assert.Equal("", g.Name);
        Assert.Equal(new[] { "n1" }, g.ItemIds);
    }

    [Fact]
    public void Pin_RefusesFourthOnSameRope()
    {
        var lib = Loaded();
        var other = lib.AddRope("B");
        for (var i = 0; i < 3; i++) Assert.True(lib.Pin(Add(lib), 10 * i, 20));
        var fourth = Add(lib);
        Assert.False(lib.Pin(fourth, 1, 1));
        Assert.False(fourth.Pinned);
        Assert.Equal(3, lib.PinCount("default"));
        Assert.True(lib.Pin(Add(lib, other.Id), 1, 1));
    }

    [Fact]
    public void Pin_RemovesFromGroupAndSetsFields()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        lib.JoinGroup(b, lib.CreateGroup(a, "g").Id, 1);
        var fired = 0;
        lib.Relocated += _ => fired++;

        Assert.True(lib.Pin(a, 5, 6));
        Assert.Null(lib.GroupOf(a));
        Assert.Equal((5d, 6d), (a.PinLeft, a.PinTop));
        Assert.Equal(1, fired);
        Assert.Equal(new[] { b.Id }, Assert.Single(lib.Groups).ItemIds);
    }

    [Fact]
    public void MovePin_SavesWithoutEvent_AndUnpinResetsPosition()
    {
        var lib = Loaded();
        var a = Add(lib);
        lib.Pin(a, 1, 1);
        var fired = 0;
        lib.Relocated += _ => fired++;
        lib.MovePin(a, 9, 8);
        Assert.Equal((9d, 8d), (a.PinLeft, a.PinTop));
        Assert.Equal(0, fired);

        lib.Unpin(a);
        Assert.False(a.Pinned);
        Assert.Equal(-1, a.Position);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void JoinGroup_UnpinsCard()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var g = lib.CreateGroup(a, "g");
        lib.Pin(b, 1, 1);
        lib.JoinGroup(b, g.Id, 1);
        Assert.False(b.Pinned);
        Assert.Equal(new[] { a.Id, b.Id }, g.ItemIds);
    }

    [Fact]
    public void JoinGroup_InsertsAtIndexAndLeavesOldGroup()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var c = Add(lib);
        var d = Add(lib);
        var g1 = lib.CreateGroup(a, "1");
        lib.JoinGroup(b, g1.Id, 5); // clamped to the end
        Assert.Equal(new[] { a.Id, b.Id }, g1.ItemIds);
        var g2 = lib.CreateGroup(c, "2");
        lib.JoinGroup(d, g2.Id, 0);
        Assert.Equal(new[] { d.Id, c.Id }, g2.ItemIds);

        lib.JoinGroup(a, g2.Id, 1);
        Assert.Equal(new[] { d.Id, a.Id, c.Id }, g2.ItemIds);
        Assert.Equal(new[] { b.Id }, g1.ItemIds);
        lib.JoinGroup(b, g2.Id, 0);
        Assert.DoesNotContain(g1, lib.Groups);
    }

    [Fact]
    public void JoinGroup_ReordersWithinSameGroup()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var g = lib.CreateGroup(a, "g");
        lib.JoinGroup(b, g.Id, 1);
        lib.JoinGroup(b, g.Id, 0);
        Assert.Equal(new[] { b.Id, a.Id }, g.ItemIds);
        lib.LeaveGroup(b);
        lib.JoinGroup(a, g.Id, 0); // sole member rejoining its own group keeps it alive
        Assert.Equal(new[] { a.Id }, Assert.Single(lib.Groups).ItemIds);
    }

    [Fact]
    public void CreateGroup_SitsAtFirstCardPositionOnItsRope()
    {
        var lib = Loaded();
        var rope = lib.AddRope("B");
        var a = Add(lib, rope.Id);
        a.Position = 0.3;
        var g = lib.CreateGroup(a, "g");
        Assert.Equal((0.3, rope.Id, "g"), (g.Position, g.RopeId, g.Name));
        Assert.Same(g, lib.GroupOf(a));
    }

    [Fact]
    public void Discard_RemovesFromGroupAndUnpins()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var g = lib.CreateGroup(a, "g");
        lib.JoinGroup(b, g.Id, 1);
        lib.Discard(a);
        Assert.Equal(new[] { b.Id }, g.ItemIds);
        lib.Discard(b);
        Assert.Empty(lib.Groups);

        var p = Add(lib);
        lib.Pin(p, 1, 1);
        lib.Discard(p);
        Assert.False(p.Pinned);
    }

    [Fact]
    public void Drop_AndMoveToRope_LeaveGroup()
    {
        var lib = Loaded();
        var rope = lib.AddRope("B");
        var a = Add(lib);
        var b = Add(lib);
        var c = Add(lib);
        var g = lib.CreateGroup(a, "g");
        lib.JoinGroup(b, g.Id, 1);
        lib.JoinGroup(c, g.Id, 2);
        lib.Drop(a);
        lib.MoveToRope(b, rope.Id);
        Assert.Equal(new[] { c.Id }, g.ItemIds);
    }

    [Fact]
    public void DeleteRope_DissolvesGroupsAndUnpins()
    {
        var lib = Loaded();
        var rope = lib.AddRope("B");
        var a = Add(lib, rope.Id);
        var b = Add(lib, rope.Id);
        lib.JoinGroup(b, lib.CreateGroup(a, "g").Id, 1);
        var p = Add(lib, rope.Id);
        lib.Pin(p, 1, 1);

        Assert.True(lib.DeleteRope(rope.Id));
        Assert.Empty(lib.Groups);
        Assert.All(new[] { a, b, p }, i => { Assert.Equal("default", i.RopeId); Assert.False(i.Pinned); });
    }

    [Fact]
    public void Ungroup_AppliesPositionsAndRemovesGroup()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var g = lib.CreateGroup(a, "g");
        lib.JoinGroup(b, g.Id, 1);
        lib.Ungroup(g.Id, new Dictionary<string, double> { [a.Id] = 0.1, [b.Id] = 0.2 });
        Assert.Empty(lib.Groups);
        Assert.Equal((0.1, 0.2), (a.Position, b.Position));
    }

    [Fact]
    public void RenameFoldMove_UpdateGroup()
    {
        var lib = Loaded();
        var g = lib.CreateGroup(Add(lib), "g");
        lib.RenameGroup(g.Id, "new");
        lib.SetFolded(g.Id, true);
        lib.MoveGroup(g.Id, 0.7);
        Assert.Equal(("new", true, 0.7), (g.Name, g.Folded, g.Position));
    }

    [Fact]
    public void Load_RepairsDanglingGroupReferences()
    {
        File.WriteAllText(AppPaths.ItemsFile, """
            {"Version":2,"Ropes":[{"Id":"default","Name":"J"},{"Id":"r2","Name":"B"}],
             "Groups":[
               {"Id":"g1","RopeId":"default","ItemIds":["n1","missing","n2","n1"]},
               {"Id":"g2","RopeId":"default","ItemIds":["missing","n2"]},
               {"Id":"g3","RopeId":"gone","ItemIds":["n1"]},
               {"Id":"g4","RopeId":"default","ItemIds":[]}],
             "Items":[{"Id":"n1","Kind":"Note","RopeId":"default"},{"Id":"n2","Kind":"Note","RopeId":"r2"}],
             "Discarded":[]}
            """);
        var lib = Loaded();
        var g = Assert.Single(lib.Groups);
        Assert.Equal("g1", g.Id);
        Assert.Equal(new[] { "n1" }, g.ItemIds);
    }

    [Fact]
    public void Load_TreatsNullListsAsEmpty()
    {
        File.WriteAllText(AppPaths.ItemsFile,
            """{"Version":2,"Ropes":null,"Groups":null,"Items":[{"Id":"n1","Kind":"Note"}],"Discarded":null}""");
        var lib = Loaded();
        Assert.Single(lib.Items);
        Assert.Empty(lib.Groups);
    }

    [Fact]
    public void GroupsAndPinsRoundTrip()
    {
        var lib = Loaded();
        var a = Add(lib);
        var b = Add(lib);
        var p = Add(lib);
        var g = lib.CreateGroup(b, "g");
        lib.JoinGroup(a, g.Id, 0);
        lib.SetFolded(g.Id, true);
        lib.Pin(p, 12.5, 34.5);
        lib.SaveNow();

        var again = Loaded();
        var g2 = Assert.Single(again.Groups);
        Assert.Equal(new[] { a.Id, b.Id }, g2.ItemIds);
        Assert.True(g2.Folded);
        var p2 = again.Items.Single(i => i.Id == p.Id);
        Assert.Equal((true, 12.5, 34.5), (p2.Pinned, p2.PinLeft, p2.PinTop));
    }
}
