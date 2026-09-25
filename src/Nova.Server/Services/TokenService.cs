using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Nova.Server.Models;

namespace Nova.Server.Services;

public sealed class TokenService(IConfiguration configuration)
{
    public string Create(User user)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username)
        };

        var token = new JwtSecurityToken(
            issuer: "nova",
            audience: "nova-client",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static ClaimsPrincipal? Validate(string token, SymmetricSecurityKey key)
    {
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(
                token,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "nova",
                    ValidateAudience = true,
                    ValidAudience = "nova-client",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                },
                out _);
        }
        catch
        {
            return null;
        }
    }
}
