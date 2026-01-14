namespace TheUtils.DbPostgresqlTests;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Test entity for Db monad tests.
/// </summary>
[Table("users")]
public class User
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Column("email")]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Column("balance")]
    public decimal Balance { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Test entity for transaction tests.
/// </summary>
[Table("accounts")]
public class Account
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("balance")]
    public decimal Balance { get; set; }

    [Column("currency")]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";
}

/// <summary>
/// Test entity for JSONB tests.
/// </summary>
[Table("documents")]
public class Document
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("title")]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Column("metadata", TypeName = "jsonb")]
    public string Metadata { get; set; } = "{}";
}
