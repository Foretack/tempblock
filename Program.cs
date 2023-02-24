using System.Net;
using LiteDB;

Console.WriteLine($"[{args[0]}][{args[1]}]");
WebApplicationBuilder builder = WebApplication.CreateBuilder();
WebApplication app = builder.Build();
string baseAddress = "https://api.twitch.tv/helix";
HttpClient client = new()
{
    DefaultRequestHeaders =
    {
        { "Authorization", "Bearer " + args[0] },
        { "Client-Id", args[1] }
    }
};
var db = new LiteDatabase("BlockedUsers.db");
ILiteCollection<BlockedUser> dbUsers = db.GetCollection<BlockedUser>("blocked_users");
List<BlockedUser> blockedUsers = new();
foreach (BlockedUser user in dbUsers.FindAll())
    blockedUsers.Add(user);

app.MapPut("/add", async (string targetId, int hours) =>
{
    if (targetId is null || hours == 0)
        return Results.BadRequest();
    if (blockedUsers.Any(x => x.Id == targetId))
        return Results.Conflict();

    HttpStatusCode code = await BlockUser(targetId);
    if (code == HttpStatusCode.NoContent)
    {
        BlockedUser target = new()
        {
            Id = targetId,
            BlockedUntil = DateTime.Now.AddHours(hours)
        };
        _ = dbUsers.Insert(target);
        blockedUsers.Add(target);
        Console.WriteLine($"Blocked {targetId} for {hours}m");
        return Results.Ok();
    }

    return Results.StatusCode((int)code);
});

_ = new Timer(async _ =>
{
    foreach (BlockedUser blockedUser in blockedUsers
    .Where(u => DateTime.Now >= u.BlockedUntil)
    .ToArray())
    {
        if (await UnblockUser(blockedUser.Id))
        {
            await Console.Out.WriteLineAsync($"Unblocked {blockedUser.Id}");
            _ = dbUsers.DeleteMany(x => x.Id == blockedUser.Id);
            _ = blockedUsers.Remove(blockedUser);
        }

        dbUsers = db.GetCollection<BlockedUser>("blocked_users");
    }
}, null, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));

app.Run("http://localhost:1340");

async Task<HttpStatusCode> BlockUser(string targetId)
{
    HttpResponseMessage response = await client.PutAsync($"{baseAddress}/users/blocks?target_user_id={targetId}", null);
    return response.StatusCode;
}

async Task<bool> UnblockUser(string targetId)
{
    HttpResponseMessage response = await client.DeleteAsync($"{baseAddress}/users/blocks?target_user_id={targetId}");
    return response.IsSuccessStatusCode;
}

internal class BlockedUser
{
    public string Id { get; set; } = default!;
    public DateTime BlockedUntil { get; set; }
}