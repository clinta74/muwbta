using Muwbta.Domain.Worlds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

internal sealed class GameConfigurationConfiguration : IEntityTypeConfiguration<GameConfiguration>
{
    public void Configure(EntityTypeBuilder<GameConfiguration> builder)
    {
        builder.ToTable("game_configurations");

        builder.HasKey(c => c.Key);
        builder.Property(c => c.Key)
            .HasColumnName("key")
            .HasMaxLength(GameConfiguration.MaxKeyLength);

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(GameConfiguration.MaxNameLength)
            .IsRequired();

        builder.Property(c => c.Description).HasColumnName("description").IsRequired();

        builder.Property(c => c.StartingRoomKey)
            .HasColumnName("starting_room_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(c => c.WelcomeMessage)
            .HasColumnName("welcome_message")
            .HasMaxLength(GameConfiguration.MaxWelcomeLength)
            .IsRequired();

        // Text with no length: the cap is in code, and the real limit is the assist's token
        // budget rather than anything the database should decide.
        builder.Property(c => c.Canon)
            .HasColumnName("canon")
            .IsRequired()
            .HasDefaultValue(string.Empty);

        builder.Property(c => c.WorldKeys)
            .HasColumnName("world_keys")
            .HasColumnType("text[]")
            .IsRequired();

        // A short list read only alongside its row, so jsonb rather than a join table - the same
        // argument an item template's Paths makes.
        builder.Property(c => c.StartingKit)
            .HasColumnName("starting_kit")
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer
                    .Deserialize<List<StartingKitItem>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<StartingKitItem>(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<StartingKitItem>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (h, k) => HashCode.Combine(h, k.GetHashCode())),
                    v => v.ToList()))
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(c => c.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // "Exactly one is live" enforced by the database rather than by everyone remembering to
        // clear the old one first. A filtered unique index is the right shape: it constrains only
        // the true rows, so any number may be inactive and at most one may not be.
        builder.HasIndex(c => c.IsActive)
            .IsUnique()
            .HasFilter("is_active")
            .HasDatabaseName("ix_game_configurations_active");
    }
}
