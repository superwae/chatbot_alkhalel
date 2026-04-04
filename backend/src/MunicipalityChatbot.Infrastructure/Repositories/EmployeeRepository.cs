using Microsoft.EntityFrameworkCore;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Db;

namespace MunicipalityChatbot.Infrastructure.Repositories;

public sealed class EmployeeRepository(AppDbContext db) : IEmployeeRepository
{
    public async Task<EmployeeUser?> GetByUsernameAsync(string username, CancellationToken ct)
    {
        var u = (username ?? "").Trim().ToLowerInvariant();
        return await db.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Username.ToLower() == u, ct);
    }
}

