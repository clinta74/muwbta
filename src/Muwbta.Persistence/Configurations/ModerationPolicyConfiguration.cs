using Muwbta.Domain.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

internal sealed class ModerationPolicyConfiguration : IEntityTypeConfiguration<ModerationPolicy>
{
    public void Configure(EntityTypeBuilder<ModerationPolicy> builder)
    {
        builder.ToTable("moderation_policy");

        // Keyed by a constant, so the single row is enforced by the primary key rather than by
        // everyone who writes to it remembering there is only meant to be one.
        builder.HasKey(p => p.Key);
        builder.Property(p => p.Key).HasColumnName("key").HasMaxLength(32);

        builder.Property(p => p.BlockedWords)
            .HasColumnName("blocked_words")
            .HasMaxLength(ModerationPolicy.MaxBlockedWordsLength)
            .IsRequired();
    }
}
