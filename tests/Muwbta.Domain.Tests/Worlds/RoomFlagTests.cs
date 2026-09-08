using Muwbta.Domain.Worlds;

namespace Muwbta.Domain.Tests.Worlds;

/// <summary>
/// PLAN.md §4.10. The load-bearing property is the first test: an unflagged room is not PvP.
/// Every other safety claim in §4.11 rests on that being true by construction.
/// </summary>
public sealed class RoomFlagTests
{
    [Fact]
    public void A_room_with_no_flags_at_all_is_not_pvp()
    {
        var resolved = RoomFlags.Resolve(RoomFlags.Pvp, new FlagSet(), new FlagSet(), new FlagSet());

        Assert.False(resolved.Value);
        Assert.Equal(FlagSource.Default, resolved.Source);
    }

    [Fact]
    public void Every_registered_flag_defaults_to_the_harmless_value()
    {
        // §4.10 requires absence to be safe. If a flag ever wants to default true it has to
        // argue for it here first, rather than sliding in as an ordinary registration.
        //
        // A text flag's harmless value is its first choice rather than false, so the claim is
        // stated per kind: no boolean defaults on, and no text flag defaults to something the
        // registry does not list.
        Assert.All(
            RoomFlags.All.Where(f => f.Kind is RoomFlagKind.Boolean),
            flag => Assert.False(flag.DefaultBoolean));

        Assert.All(
            RoomFlags.All.Where(f => f.Kind is RoomFlagKind.Text),
            flag => Assert.True(flag.Accepts(flag.DefaultText), $"{flag.Key} defaults off-list"));
    }

    [Fact]
    public void Missing_levels_resolve_to_the_default()
    {
        // Null zone and world happen while a room is being built and nothing is loaded yet.
        var resolved = RoomFlags.Resolve(RoomFlags.Pvp, null, null, null);

        Assert.False(resolved.Value);
        Assert.Equal(FlagSource.Default, resolved.Source);
    }

    [Fact]
    public void The_room_wins_over_the_zone_and_the_world()
    {
        var room = new FlagSet();
        room.Set(RoomFlags.Pvp.Key, false);

        var zone = new FlagSet();
        zone.Set(RoomFlags.Pvp.Key, true);

        var world = new FlagSet();
        world.Set(RoomFlags.Pvp.Key, true);

        var resolved = RoomFlags.Resolve(RoomFlags.Pvp, room, zone, world);

        Assert.False(resolved.Value);
        Assert.Equal(FlagSource.Room, resolved.Source);
        Assert.False(resolved.IsInherited);
    }

    [Fact]
    public void The_zone_wins_over_the_world()
    {
        var zone = new FlagSet();
        zone.Set(RoomFlags.Pvp.Key, true);

        var world = new FlagSet();
        world.Set(RoomFlags.Pvp.Key, false);

        var resolved = RoomFlags.Resolve(RoomFlags.Pvp, new FlagSet(), zone, world);

        Assert.True(resolved.Value);
        Assert.Equal(FlagSource.Zone, resolved.Source);
        Assert.True(resolved.IsInherited);
    }

    [Fact]
    public void The_world_applies_when_neither_room_nor_zone_declares_it()
    {
        var world = new FlagSet();
        world.Set(RoomFlags.Pvp.Key, true);

        var resolved = RoomFlags.Resolve(RoomFlags.Pvp, new FlagSet(), new FlagSet(), world);

        Assert.True(resolved.Value);
        Assert.Equal(FlagSource.World, resolved.Source);
    }

    [Fact]
    public void An_explicit_false_overrides_inheritance_but_clearing_restores_it()
    {
        // The difference between "off" and "absent" is the whole point of the chain: one is a
        // decision about this room, the other is a decision to defer to the zone.
        var zone = new FlagSet();
        zone.Set(RoomFlags.Pvp.Key, true);

        var room = new FlagSet();
        room.Set(RoomFlags.Pvp.Key, false);
        Assert.False(RoomFlags.Resolve(RoomFlags.Pvp, room, zone, null).Value);

        room.Clear(RoomFlags.Pvp.Key);
        Assert.True(RoomFlags.Resolve(RoomFlags.Pvp, room, zone, null).Value);
    }

    [Fact]
    public void A_wrong_typed_value_is_treated_as_absent()
    {
        // "pvp": "yes" is the classic hand-edited-jsonb mistake. It must not read as truthy;
        // it falls through to the default, which is safe (PLAN.md §7.4).
        var room = new FlagSet();
        room.Set(RoomFlags.Pvp.Key, FlagValue.Of("yes"));

        var resolved = RoomFlags.Resolve(RoomFlags.Pvp, room, null, null);

        Assert.False(resolved.Value);
        Assert.Equal(FlagSource.Default, resolved.Source);
    }

    [Fact]
    public void A_wrong_typed_value_at_one_level_does_not_block_the_next()
    {
        var room = new FlagSet();
        room.Set(RoomFlags.Pvp.Key, FlagValue.Of(1));

        var zone = new FlagSet();
        zone.Set(RoomFlags.Pvp.Key, true);

        Assert.True(RoomFlags.Resolve(RoomFlags.Pvp, room, zone, null).Value);
    }

    [Fact]
    public void Unknown_keys_are_kept_and_reported()
    {
        var flags = new FlagSet();
        flags.Set("someFutureFlag", true);
        flags.Set(RoomFlags.Dark.Key, true);

        Assert.True(flags.Has("someFutureFlag"));
        Assert.Equal(["someFutureFlag"], flags.UnknownKeys);
    }

    [Fact]
    public void Registry_lookups_are_exact_and_case_sensitive()
    {
        Assert.True(RoomFlags.IsKnown("pvp"));
        Assert.False(RoomFlags.IsKnown("PVP"));
        Assert.False(RoomFlags.IsKnown("pvpp"));
        Assert.False(RoomFlags.IsKnown(null));

        Assert.Same(RoomFlags.Pvp, RoomFlags.Find("pvp"));
    }

    [Fact]
    public void Flag_keys_are_unique_and_every_flag_has_a_summary()
    {
        Assert.Equal(RoomFlags.All.Count, RoomFlags.All.Select(f => f.Key).Distinct().Count());
        Assert.All(RoomFlags.All, f => Assert.False(string.IsNullOrWhiteSpace(f.Summary)));
    }

    [Fact]
    public void Cloning_a_flag_set_does_not_alias_the_original()
    {
        var original = new FlagSet();
        original.Set(RoomFlags.Dark.Key, true);

        var copy = original.Clone();
        copy.Set(RoomFlags.Dark.Key, false);

        Assert.True(original.BooleanOrNull(RoomFlags.Dark.Key));
        Assert.False(copy.BooleanOrNull(RoomFlags.Dark.Key));
    }

    [Fact]
    public void Content_equality_ignores_insertion_order()
    {
        var left = new FlagSet();
        left.Set(RoomFlags.Dark.Key, true);
        left.Set(RoomFlags.Pvp.Key, false);

        var right = new FlagSet();
        right.Set(RoomFlags.Pvp.Key, false);
        right.Set(RoomFlags.Dark.Key, true);

        Assert.True(left.ContentEquals(right));

        right.Set(RoomFlags.Indoors.Key, true);
        Assert.False(left.ContentEquals(right));
    }
}
