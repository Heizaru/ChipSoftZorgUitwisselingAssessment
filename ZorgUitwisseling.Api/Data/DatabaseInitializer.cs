using Microsoft.EntityFrameworkCore;
using ZorgUitwisseling.Api.Domain;

namespace ZorgUitwisseling.Api.Data;

public static class DatabaseInitializer
{
    public async static Task InitializeAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        if (!await db.Organizations.AnyAsync(o => o.Id == "ziekenhuis-a", ct))
        {
            db.Organizations.Add(new Organization { Id = "ziekenhuis-a", Name = "Ziekenhuis A" });
        }

        if (!await db.Organizations.AnyAsync(o => o.Id == "verpleeghuis-b", ct))
        {
            db.Organizations.Add(new Organization { Id = "verpleeghuis-b", Name = "Verpleeghuis B" });
        }

        await db.SaveChangesAsync(ct);
    }
}
