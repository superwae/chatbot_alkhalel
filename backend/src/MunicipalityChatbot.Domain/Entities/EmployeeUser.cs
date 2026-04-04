namespace MunicipalityChatbot.Domain.Entities;

public static class EmployeeRoles
{
    public const string EmployeeAdmin = "EmployeeAdmin";
    public const string EmployeeEditor = "EmployeeEditor";
    public const string EmployeeViewer = "EmployeeViewer";
}

public sealed class EmployeeUser
{
    public Guid EmployeeId { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = EmployeeRoles.EmployeeViewer;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

