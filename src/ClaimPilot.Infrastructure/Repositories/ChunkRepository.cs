using Microsoft.EntityFrameworkCore;

using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Domain.Entities;

namespace ClaimPilot.Infrastructure.Repositories;

public sealed class ChunkRepository : IChunkRepository
{
    private readonly Data.AppDbContext _db;

    public ChunkRepository(Data.AppDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsByHashAsync(string contentHash, CancellationToken ct)
        => _db.PolicyChunks.AsNoTracking().AnyAsync(c => c.ContentHash == contentHash, ct);

    public Task AddAsync(PolicyChunk chunk, CancellationToken ct)
    {
        _db.PolicyChunks.Add(chunk);
        return _db.SaveChangesAsync(ct);
    }

    public Task AddRangeAsync(IEnumerable<PolicyChunk> chunks, CancellationToken ct)
    {
        _db.PolicyChunks.AddRange(chunks);
        return _db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}