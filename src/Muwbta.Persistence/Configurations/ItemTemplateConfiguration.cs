using Muwbta.Domain.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

internal sealed class ItemTemplateConfiguration : IEntityTypeConfiguration<ItemTemplate>
{
    public void Configure(EntityTypeBuilder<ItemTemplate> builder)
    {
        builder.ToTable("item_templates");

        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasColumnName("key").ValueGeneratedNever();

        builder.Property(e => e.Name).HasColumnName("name");
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.Icon).HasColumnName("icon");
        builder.Property(e => e.IsTwoHanded).HasColumnName("is_two_handed");
        builder.Property(e => e.Weight).HasColumnName("weight");
        builder.Property(e => e.BaseValue).HasColumnName("base_value");
        builder.Property(e => e.BaseStats).HasColumnName("base_stats").HasColumnType("jsonb");
        builder.Property(e => e.AttackDelayPulses).HasColumnName("attack_delay_pulses");
        builder.Property(e => e.AttackVerb).HasColumnName("attack_verb");
        builder.Property(e => e.IsQuestItem).HasColumnName("is_quest_item");
        builder.Property(e => e.IsLore).HasColumnName("is_lore");
        builder.Property(e => e.IsNoDrop).HasColumnName("is_no_drop");
        builder.Property(e => e.IsLightSource).HasColumnName("is_light_source");
        builder.Property(e => e.FoodValue).HasColumnName("food_value");
        builder.Property(e => e.DrinkValue).HasColumnName("drink_value");

        // The same jsonb shape an ability's effect list is stored in, because it is that list.
        builder.Property(e => e.UseEffects)
            .HasColumnName("use_effects")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();
        builder.Property(e => e.UseCooldownPulses).HasColumnName("use_cooldown_pulses");

        // The slots it may fill, as a jsonb array of names - same shape and same argument as
        // Paths below, and stored as names rather than the enum's numbers so a slot inserted into
        // the middle of ItemSlot later does not silently reinterpret every row.
        builder.Property(e => e.Slots)
            .HasColumnName("slots")
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(
                    v.Select(s => s.ToString()), (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer
                        .Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null)!
                        .Select(Enum.Parse<ItemSlot>)
                        .ToList(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<ItemSlot>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                    v => v.ToList()))
            .IsRequired();

        // The Paths that may equip it, as a jsonb array of names. A join table would be the
        // relational answer and is not worth it: this is a short, unordered, read-only list that
        // is only ever wanted alongside the row it belongs to.
        builder.Property(e => e.Paths)
            .HasColumnName("paths")
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(
                    v.Select(p => p.ToString()), (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer
                        .Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null)!
                        .Select(Enum.Parse<Muwbta.Domain.Characters.CharacterPath>)
                        .ToList(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<Muwbta.Domain.Characters.CharacterPath>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (h, p) => HashCode.Combine(h, p.GetHashCode())),
                    v => v.ToList()))
            .IsRequired();
    }
}
