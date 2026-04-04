using MunicipalityChatbot.Infrastructure.Security;

if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.WriteLine("Usage: dotnet run --project backend/tools/PasswordHashTool -- <password>");
    return;
}

var hasher = new PasswordHasher();
var hash = hasher.Hash(args[0]);
Console.WriteLine(hash);

