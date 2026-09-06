using Muwbta.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

internal sealed class AccessTokenConfiguration : IEntityTypeConfiguration<AccessToken>
{
    public void Configure(EntityTypeBuilder<AccessToken> builder)
    {
        builder.ToTable("access_tokens");

        // The key is the token's own public half, carried in the token string. That is what makes
        // verification an indexed lookup rather than a scan (docs/PAT-AND-MCP.md §2), so it is not
        // database-generated: the server mints the id and the secret together.
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        // Cascade, unlike admin_audit's deliberate lack of a foreign key. The audit row is a
        // record of something that happened and outlives the account; a credential for a deleted
        // account is not a record of anything, it is a live key to a door that no longer exists.
        builder.Property(t => t.AccountId).HasColumnName("account_id").IsRequired();
        builder.HasOne(t => t.Account)
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(64).IsRequired();

        // Base64 of a SHA-256 is always 44 characters. Sized to it rather than left free so a
        // column that starts holding something else is visible as a change.
        builder.Property(t => t.SecretHash).HasColumnName("secret_hash").HasMaxLength(64).IsRequired();

        builder.Property(t => t.Scope)
            .HasColumnName("scope")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");
        builder.Property(t => t.PasswordChangedAt).HasColumnName("password_changed_at");

        // "This account's tokens, newest first" - the list view, and the cap check on issue.
        builder.HasIndex(t => new { t.AccountId, t.CreatedAt })
            .HasDatabaseName("ix_access_tokens_account")
            .IsDescending(false, true);
    }
}
