namespace TheUtils.Persistence;

using Microsoft.EntityFrameworkCore;

public interface HasDbContext
{
    DbContext DbContext { get; }
}
