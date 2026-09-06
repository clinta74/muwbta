using Muwbta.Domain.Worlds;
using Muwbta.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

internal sealed class WorldConfiguration : IEntityTypeConfiguration<World>
{
    public void Configure(EntityTypeBuilder<World> builder)
    {
        builder.ToTable("worlds");

        builder.HasKey(w => w.Key);
        builder.Property(w => w.Key).HasColumnName("key").HasMaxLength(32);

        builder.Property(w => w.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder.Property(w => w.Description).HasColumnName("description").IsRequired();
        builder.Property(w => w.SortOrder).HasColumnName("sort_order").IsRequired();

        builder.Property(w => w.Multipliers)
            .HasColumnName("multipliers")
            .HasColumnType("jsonb")
            .HasConversion(new MultipliersConverter())
            .IsRequired();

        builder.Property(w => w.Flags)
            .HasColumnName("flags")
            .HasColumnType("jsonb")
            .HasConversion(new FlagSetConverter(), new FlagSetComparer())
            .IsRequired();

        builder.HasMany(w => w.Zones)
            .WithOne(z => z.World)
            .HasForeignKey(z => z.WorldKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WorldMapConfiguration : IEntityTypeConfiguration<WorldMap>
{
    public void Configure(EntityTypeBuilder<WorldMap> builder)
    {
        builder.ToTable("world_maps");

        // Keyed by the world it draws, which is what makes "at most one sheet per world" a
        // property of the schema rather than of whoever writes to it. No foreign key to worlds:
        // the importer applies collections one kind at a time and a sheet may legitimately land
        // beside a world in the same bundle, so ordering would be the only thing enforcing it.
        builder.HasKey(m => m.WorldKey);
        builder.Property(m => m.WorldKey).HasColumnName("world_key").HasMaxLength(32);

        // Unbounded: the sheets run to about 190 KB and a cap here would be a number nobody could
        // defend. Never loaded by the game loop - only MapSheets reads this table.
        builder.Property(m => m.Svg).HasColumnName("svg").IsRequired();
    }
}

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("zones");

        builder.HasKey(z => z.Key);
        builder.Property(z => z.Key).HasColumnName("key").HasMaxLength(64);

        builder.Property(z => z.WorldKey).HasColumnName("world_key").HasMaxLength(32).IsRequired();
        builder.HasIndex(z => z.WorldKey).HasDatabaseName("ix_zones_world_key");

        builder.Property(z => z.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder.Property(z => z.Description).HasColumnName("description").IsRequired();
        builder.Property(z => z.MinLevel).HasColumnName("min_level").IsRequired();
        builder.Property(z => z.MaxLevel).HasColumnName("max_level").IsRequired();

        builder.Property(z => z.Multipliers)
            .HasColumnName("multipliers")
            .HasColumnType("jsonb")
            .HasConversion(new MultipliersConverter())
            .IsRequired();

        builder.Property(z => z.Flags)
            .HasColumnName("flags")
            .HasColumnType("jsonb")
            .HasConversion(new FlagSetConverter(), new FlagSetComparer())
            .IsRequired();

        builder.HasMany(z => z.Rooms)
            .WithOne(r => r.Zone)
            .HasForeignKey(r => r.ZoneKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
