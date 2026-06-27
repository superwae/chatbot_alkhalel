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

        //   "pbkdf2$120000$ZIuq9lA/aB+jlCOpyi3OZA==$0UrekWum1ST/Y1kvMIZLRsuOonmTykATQlnNWjU17wI=",

     public async Task<IEnumerable<EmployeeUser>> GetAllTest(CancellationToken ct)
    {
        var newEmployees = new EmployeeUser(){
            EmployeeId = Guid.NewGuid(),
            Username = "employee",
            PasswordHash = "pbkdf2$120000$uf0Q9TMp85j7xTRoLkfH1Q==$QwEnTS4GpYlAsb53TDqrnmpKMiy8k/u3OLSskrXqJ0Y=",
            Role = "EmployeeViewer",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        db.Employees.Add(newEmployees);
        await db.SaveChangesAsync(ct);

        return await db.Employees.AsNoTracking().ToListAsync();
    }
}

