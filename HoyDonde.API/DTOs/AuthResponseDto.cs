public class AuthResponseDto
{
    public string Token { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public List<string>? Roles { get; set; }
}